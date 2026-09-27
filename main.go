package main

import (
	"context"
	"crypto/rand"
	"embed"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"math"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/Microsoft/go-winio"
	"github.com/gorilla/websocket"
	"golang.org/x/sys/windows"
)

//go:embed src/AnnoPipeHub/wwwroot/* examples/browser-client.html
var embeddedFiles embed.FS

const (
	dashboardAddr   = "127.0.0.1:8765"
	defaultDataPort = 8766
	protocolVersion = 2
	maxFrameSize    = 1024 * 1024
	maxDebugEntries = 200
	clientQueueSize = 64
)

type PipeState string

const (
	Waiting      PipeState = "Waiting"
	Connected    PipeState = "Connected"
	Disconnected PipeState = "Disconnected"
	Timeout      PipeState = "Timeout"
	NotFound     PipeState = "NotFound"
	Busy         PipeState = "Busy"
)

type ListenerSettings struct {
	Port       int     `json:"port"`
	LANEnabled bool    `json:"lanEnabled"`
	LANAddress *string `json:"lanAddress"`
}
type ListenerStatus struct {
	LANEnabled       bool    `json:"lanEnabled"`
	BindAddress      string  `json:"bindAddress"`
	Port             int     `json:"port"`
	State            string  `json:"state"`
	ConnectedClients int     `json:"connectedClients"`
	Error            *string `json:"error"`
}
type HubStatus struct {
	HubStatus        string     `json:"hubStatus"`
	PipeStatus       PipeState  `json:"pipeStatus"`
	ConnectedClients int        `json:"connectedClients"`
	LastMessageAtUTC *time.Time `json:"lastMessageAtUtc"`
	ProtocolVersion  *int       `json:"protocolVersion"`
	Error            *string    `json:"error"`
}
type FileLogStatus struct {
	Enabled   bool    `json:"enabled"`
	Available bool    `json:"available"`
	Path      *string `json:"path"`
	Error     *string `json:"error"`
}
type ConnectedClient struct {
	ID             string    `json:"id"`
	ConnectedAtUTC time.Time `json:"connectedAtUtc"`
	Endpoint       string    `json:"endpoint"`
}
type PipeError struct {
	Category     string         `json:"category"`
	Severity     string         `json:"severity"`
	APIOperation string         `json:"apiOperation"`
	WindowsCode  int            `json:"windowsCode"`
	WindowsName  string         `json:"windowsName"`
	ErrorText    string         `json:"errorText"`
	GoErrorType  string         `json:"goErrorType"`
	EventKey     string         `json:"eventKey"`
	Parameters   map[string]any `json:"parameters"`
}
type DebugEntry struct {
	AtUTC           time.Time      `json:"atUtc"`
	Kind            string         `json:"kind"`
	Message         string         `json:"message"`
	MessageSize     *int           `json:"messageSize"`
	ProtocolVersion *int           `json:"protocolVersion"`
	MessageType     *string        `json:"messageType"`
	Details         any            `json:"details"`
	RawHex          *string        `json:"rawHex"`
	Error           *PipeError     `json:"error"`
	EventKey        *string        `json:"eventKey"`
	Parameters      map[string]any `json:"parameters"`
}

type ProductionEntry struct {
	ProductGUID               int         `json:"productGuid"`
	ProductGeneration         float32     `json:"productGeneration"`
	ProductConsumption        float32     `json:"productConsumption"`
	ProductDelta              float32     `json:"productDelta"`
	PerfectProductGeneration  float32     `json:"perfectProductGeneration"`
	PerfectProductConsumption float32     `json:"perfectProductConsumption"`
	AmountOfBuildings         int         `json:"amountOfBuildings"`
	TotalMaintenance          int         `json:"totalMaintenance"`
	TotalIncome               float32     `json:"totalIncome"`
	TotalProfit               int         `json:"totalProfit"`
	SummedProductivity        float32     `json:"summedProductivity"`
	AverageProductivity       float32     `json:"averageProductivity"`
	WorkforceGUIDToAmount     map[int]int `json:"workforceGuidToAmount"`
	BuildingGUIDToAmount      map[int]int `json:"buildingGuidToAmount"`
}
type Statistics struct {
	SessionID    byte              `json:"sessionId"`
	IslandID     byte              `json:"islandId"`
	AreaIndex    byte              `json:"areaIndex"`
	SessionGUID  int               `json:"sessionGuid"`
	AreaName     string            `json:"areaName"`
	RawTimestamp int64             `json:"rawTimestamp"`
	Entries      []ProductionEntry `json:"entries"`
}
type DecodedMessage struct {
	Type        byte
	Version     *int
	SessionName *string
	Statistics  *Statistics
}

type StatisticsEvent struct {
	SchemaVersion int         `json:"schemaVersion"`
	Type          string      `json:"type"`
	ReceivedAtUTC time.Time   `json:"receivedAtUtc"`
	Statistics    *Statistics `json:"statistics"`
}

type Hub struct {
	mu          sync.RWMutex
	status      HubStatus
	listener    ListenerStatus
	snapshots   map[string]Statistics
	debug       []DebugEntry
	clients     map[string]*client
	fileLog     *FileLogger
	lastDebug   string
	lastDebugAt time.Time
}

func newHub() *Hub {
	return &Hub{status: HubStatus{HubStatus: "Ready", PipeStatus: Waiting}, listener: ListenerStatus{BindAddress: "127.0.0.1", Port: defaultDataPort, State: "Stopped"}, snapshots: map[string]Statistics{}, clients: map[string]*client{}}
}
func (h *Hub) statusSnapshot() HubStatus {
	h.mu.RLock()
	defer h.mu.RUnlock()
	s := h.status
	s.ConnectedClients = len(h.clients)
	return s
}
func (h *Hub) listenerSnapshot() ListenerStatus {
	h.mu.RLock()
	defer h.mu.RUnlock()
	s := h.listener
	s.ConnectedClients = len(h.clients)
	return s
}
func (h *Hub) clientsSnapshot() []ConnectedClient {
	h.mu.RLock()
	defer h.mu.RUnlock()
	out := make([]ConnectedClient, 0, len(h.clients))
	for _, c := range h.clients {
		out = append(out, c.info)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].ConnectedAtUTC.Before(out[j].ConnectedAtUTC) })
	return out
}
func (h *Hub) setListener(s ListenerStatus) {
	h.mu.Lock()
	h.listener = s
	h.mu.Unlock()
	h.broadcastStatus()
}
func (h *Hub) setPipeState(state PipeState, eventKey string, errText *string, params map[string]any) {
	h.mu.Lock()
	h.status.PipeStatus = state
	h.status.Error = errText
	h.mu.Unlock()
	h.addDebug("pipe", eventKey, params, nil, nil, nil, nil)
	h.broadcastStatus()
}
func (h *Hub) setProtocol(version int) {
	h.mu.Lock()
	h.status.ProtocolVersion = &version
	if version == protocolVersion {
		h.status.Error = nil
	}
	h.mu.Unlock()
	key := "protocol.unsupported"
	if version == protocolVersion {
		key = "protocol.version"
	}
	h.addDebug("protocol", key, map[string]any{"version": version}, nil, &version, nil, nil)
	h.broadcastStatus()
}
func (h *Hub) addDebug(kind, key string, params map[string]any, size *int, version *int, messageType *string, details any) {
	now := time.Now().UTC()
	sig := fmt.Sprintf("%s|%s|%v|%v|%v|%v|%v", kind, key, params, size, version, messageType, details)
	h.mu.Lock()
	if (kind == "pipe" || kind == "error") && sig == h.lastDebug && now.Sub(h.lastDebugAt) < 5*time.Second {
		h.mu.Unlock()
		return
	}
	h.lastDebug = sig
	h.lastDebugAt = now
	ek := key
	e := DebugEntry{AtUTC: now, Kind: kind, Message: key, MessageSize: size, ProtocolVersion: version, MessageType: messageType, Details: details, EventKey: &ek, Parameters: params}
	h.debug = append(h.debug, e)
	if len(h.debug) > maxDebugEntries {
		h.debug = h.debug[len(h.debug)-maxDebugEntries:]
	}
	fl := h.fileLog
	h.mu.Unlock()
	if fl != nil {
		fl.enqueue(e)
	}
}
func (h *Hub) accept(m DecodedMessage, size int, raw []byte) {
	now := time.Now().UTC()
	h.mu.Lock()
	h.status.LastMessageAtUTC = &now
	h.status.Error = nil
	if m.Type == 1 || m.Type == 2 {
		h.snapshots = map[string]Statistics{}
	}
	if m.Statistics != nil {
		s := *m.Statistics
		h.snapshots[fmt.Sprintf("%d:%d:%d:%d", s.SessionGUID, s.SessionID, s.IslandID, s.AreaIndex)] = s
	}
	h.mu.Unlock()
	typ := messageType(m)
	h.addDebug("message", "message.received", map[string]any{"type": typ}, ptrInt(size), m.Version, ptrString(typ), debugDetails(m))
	h.broadcast(wireMessage(m))
	h.broadcastStatus()
}
func (h *Hub) clearSnapshots() {
	h.mu.Lock()
	h.snapshots = map[string]Statistics{}
	h.mu.Unlock()
	h.broadcast(map[string]any{"schemaVersion": 1, "type": "state.snapshot", "receivedAtUtc": time.Now().UTC(), "snapshots": []StatisticsEvent{}})
}
func (h *Hub) snapshotWire() []StatisticsEvent {
	h.mu.RLock()
	defer h.mu.RUnlock()
	out := make([]StatisticsEvent, 0, len(h.snapshots))
	for _, s := range h.snapshots {
		statistics := s
		out = append(out, StatisticsEvent{SchemaVersion: 1, Type: "area.production.statistics", ReceivedAtUTC: time.Now().UTC(), Statistics: &statistics})
	}
	return out
}
func (h *Hub) clearDebug() { h.mu.Lock(); h.debug = nil; h.mu.Unlock() }
func (h *Hub) broadcast(value any) {
	b, _ := json.Marshal(value)
	h.mu.RLock()
	clients := make([]*client, 0, len(h.clients))
	for _, c := range h.clients {
		clients = append(clients, c)
	}
	h.mu.RUnlock()
	for _, c := range clients {
		c.enqueue(string(b))
	}
}
func (h *Hub) statusWire() any {
	return map[string]any{"schemaVersion": 1, "type": "hub.status", "receivedAtUtc": time.Now().UTC(), "hub": map[string]any{"status": h.statusSnapshot(), "listener": h.listenerSnapshot()}}
}
func (h *Hub) broadcastStatus()    { h.broadcast(h.statusWire()) }
func (h *Hub) addClient(c *client) { h.mu.Lock(); h.clients[c.id] = c; h.mu.Unlock() }
func (h *Hub) removeClient(id string) {
	h.mu.Lock()
	delete(h.clients, id)
	h.mu.Unlock()
	h.broadcastStatus()
}

type client struct {
	id    string
	info  ConnectedClient
	conn  *websocket.Conn
	queue chan string
	done  chan struct{}
	once  sync.Once
}

func newClient(conn *websocket.Conn, endpoint string) *client {
	id := newID()
	return &client{id: id, info: ConnectedClient{ID: id, ConnectedAtUTC: time.Now().UTC(), Endpoint: endpoint}, conn: conn, queue: make(chan string, clientQueueSize), done: make(chan struct{})}
}
func (c *client) enqueue(msg string) {
	select {
	case c.queue <- msg:
	default:
		select {
		case <-c.queue:
		default:
		}
		select {
		case c.queue <- msg:
		default:
		}
	}
}
func (c *client) close() { c.once.Do(func() { close(c.done); close(c.queue); _ = c.conn.Close() }) }
func (c *client) sendLoop() {
	for msg := range c.queue {
		if err := c.conn.WriteMessage(websocket.TextMessage, []byte(msg)); err != nil {
			return
		}
	}
}

func newID() string              { b := make([]byte, 16); _, _ = rand.Read(b); return hex.EncodeToString(b) }
func ptrInt(v int) *int          { return &v }
func ptrString(v string) *string { return &v }
func messageType(m DecodedMessage) string {
	switch m.Type {
	case 0:
		return "Version"
	case 1:
		return "SessionStart"
	case 2:
		return "SessionEnd"
	case 3:
		return "AreaProductionStatistics"
	}
	return "Unknown"
}
func debugDetails(m DecodedMessage) any {
	if m.Type == 0 {
		return map[string]any{"version": m.Version}
	}
	if m.Type == 1 {
		return map[string]any{"sessionName": m.SessionName}
	}
	if m.Statistics == nil {
		return nil
	}
	return map[string]any{"sessionId": m.Statistics.SessionID, "islandId": m.Statistics.IslandID, "areaIndex": m.Statistics.AreaIndex, "sessionGuid": m.Statistics.SessionGUID, "areaName": m.Statistics.AreaName, "rawTimestamp": m.Statistics.RawTimestamp, "productionEntryCount": len(m.Statistics.Entries)}
}
func wireMessage(m DecodedMessage) any {
	switch m.Type {
	case 1:
		return map[string]any{"schemaVersion": 1, "type": "session.start", "receivedAtUtc": time.Now().UTC(), "session": map[string]any{"sessionName": m.SessionName}}
	case 2:
		return map[string]any{"schemaVersion": 1, "type": "session.end", "receivedAtUtc": time.Now().UTC()}
	case 3:
		return StatisticsEvent{SchemaVersion: 1, Type: "area.production.statistics", ReceivedAtUTC: time.Now().UTC(), Statistics: m.Statistics}
	default:
		return map[string]any{"schemaVersion": 1, "type": "hub.status", "receivedAtUtc": time.Now().UTC()}
	}
}

func decodeFrame(frame []byte, preamble bool) (DecodedMessage, error) {
	r := &frameReader{data: frame}
	typ, err := r.byte("Nachrichtentyp")
	if err != nil {
		return DecodedMessage{}, err
	}
	if typ > 3 {
		return DecodedMessage{}, fmt.Errorf("Unbekannter Nachrichtentyp: %d.", typ)
	}
	if preamble && typ != 0 {
		return DecodedMessage{}, errors.New("Die erste Pipe-Nachricht muss die Version enthalten.")
	}
	m := DecodedMessage{Type: typ}
	switch typ {
	case 0:
		v, e := r.i32("Protokollversion")
		if e != nil {
			return m, e
		}
		m.Version = &v
	case 1:
		s, e := r.str()
		if e != nil {
			return m, e
		}
		m.SessionName = &s
	case 2:
	default:
		s, e := decodeStats(r)
		if e != nil {
			return m, e
		}
		m.Statistics = &s
	}
	if r.pos != len(frame) {
		return m, fmt.Errorf("Nachricht enthält %d unerwartete Bytes.", len(frame)-r.pos)
	}
	return m, nil
}

type frameReader struct {
	data []byte
	pos  int
}

func (r *frameReader) need(n int, field string) error {
	if n < 0 || r.pos > len(r.data)-n {
		return fmt.Errorf("Nachricht ist bei %s abgeschnitten.", field)
	}
	return nil
}
func (r *frameReader) byte(f string) (byte, error) {
	if e := r.need(1, f); e != nil {
		return 0, e
	}
	v := r.data[r.pos]
	r.pos++
	return v, nil
}
func (r *frameReader) i32(f string) (int, error) {
	if e := r.need(4, f); e != nil {
		return 0, e
	}
	v := int(int32(binary.LittleEndian.Uint32(r.data[r.pos:])))
	r.pos += 4
	return v, nil
}
func (r *frameReader) i64(f string) (int64, error) {
	if e := r.need(8, f); e != nil {
		return 0, e
	}
	v := int64(binary.LittleEndian.Uint64(r.data[r.pos:]))
	r.pos += 8
	return v, nil
}
func (r *frameReader) f32(f string) (float32, error) {
	v, e := r.i32(f)
	return math.Float32frombits(uint32(int32(v))), e
}
func (r *frameReader) str() (string, error) {
	n, e := r.byte("String-Länge")
	if e != nil {
		return "", e
	}
	if e = r.need(int(n), "String-Inhalt"); e != nil {
		return "", e
	}
	s := string(r.data[r.pos : r.pos+int(n)])
	r.pos += int(n)
	return s, nil
}
func (r *frameReader) count(f string) (int, error) {
	v, e := r.i32(f)
	if e != nil {
		return 0, e
	}
	if v < 0 || v > maxFrameSize/8 {
		return 0, fmt.Errorf("Ungültige Anzahl für %s: %d.", f, v)
	}
	return v, nil
}
func (r *frameReader) mapValues(f string) (map[int]int, error) {
	n, e := r.count(f)
	if e != nil {
		return nil, e
	}
	out := map[int]int{}
	for i := 0; i < n; i++ {
		g, e := r.i32(f + "-GUID")
		if e != nil {
			return nil, e
		}
		a, e := r.i32(f + "-Menge")
		if e != nil {
			return nil, e
		}
		out[g] += a
	}
	return out, nil
}
func decodeStats(r *frameReader) (Statistics, error) {
	var s Statistics
	var e error
	if s.SessionID, e = r.byte("Session-ID"); e != nil {
		return s, e
	}
	if s.IslandID, e = r.byte("Insel-ID"); e != nil {
		return s, e
	}
	if s.AreaIndex, e = r.byte("Area-Index"); e != nil {
		return s, e
	}
	if s.SessionGUID, e = r.i32("Session-GUID"); e != nil {
		return s, e
	}
	if s.AreaName, e = r.str(); e != nil {
		return s, e
	}
	if s.RawTimestamp, e = r.i64("Zeitstempel"); e != nil {
		return s, e
	}
	n, e := r.count("Produktionsanzahl")
	if e != nil {
		return s, e
	}
	s.Entries = make([]ProductionEntry, 0, n)
	for i := 0; i < n; i++ {
		var p ProductionEntry
		if p.ProductGUID, e = r.i32("Produkt-GUID"); e != nil {
			return s, e
		}
		if p.ProductGeneration, e = r.f32("Erzeugung"); e != nil {
			return s, e
		}
		if p.ProductConsumption, e = r.f32("Verbrauch"); e != nil {
			return s, e
		}
		if p.ProductDelta, e = r.f32("Delta"); e != nil {
			return s, e
		}
		if p.PerfectProductGeneration, e = r.f32("perfekte Erzeugung"); e != nil {
			return s, e
		}
		if p.PerfectProductConsumption, e = r.f32("perfekter Verbrauch"); e != nil {
			return s, e
		}
		if p.AmountOfBuildings, e = r.i32("Gebäudeanzahl"); e != nil {
			return s, e
		}
		if p.TotalMaintenance, e = r.i32("Wartung"); e != nil {
			return s, e
		}
		if p.TotalIncome, e = r.f32("Einkommen"); e != nil {
			return s, e
		}
		if p.TotalProfit, e = r.i32("Gewinn"); e != nil {
			return s, e
		}
		if p.SummedProductivity, e = r.f32("SummedProductivity"); e != nil {
			return s, e
		}
		if p.AverageProductivity, e = r.f32("AverageProductivity"); e != nil {
			return s, e
		}
		if p.WorkforceGUIDToAmount, e = r.mapValues("Workforce-GUID-Menge"); e != nil {
			return s, e
		}
		if p.BuildingGUIDToAmount, e = r.mapValues("Building-GUID-Menge"); e != nil {
			return s, e
		}
		s.Entries = append(s.Entries, p)
	}
	return s, nil
}

func validPort(p int) bool { return p >= 1024 && p <= 65535 }
func isPrivate(ip net.IP) bool {
	v := ip.To4()
	return v != nil && (v[0] == 10 || (v[0] == 172 && v[1] >= 16 && v[1] <= 31) || (v[0] == 192 && v[1] == 168))
}
func allowedOrigin(origin string) bool          { return origin == "" || origin == "http://127.0.0.1:8765" }
func allowedDashboardHost(r *http.Request) bool { return strings.EqualFold(r.Host, "127.0.0.1:8765") }
func settingsPath() string {
	exe, err := os.Executable()
	if err != nil {
		return filepath.Join("config", "hub-settings.json")
	}
	return filepath.Join(filepath.Dir(exe), "config", "hub-settings.json")
}
func loadSettings() ListenerSettings {
	out := ListenerSettings{Port: defaultDataPort}
	b, e := os.ReadFile(settingsPath())
	if e != nil {
		return out
	}
	if json.Unmarshal(b, &out) != nil || !validPort(out.Port) {
		out = ListenerSettings{Port: defaultDataPort}
	}
	return out
}
func saveSettings(s ListenerSettings) error {
	if e := os.MkdirAll(filepath.Dir(settingsPath()), 0755); e != nil {
		return e
	}
	b, _ := json.MarshalIndent(s, "", "  ")
	return os.WriteFile(settingsPath(), b, 0644)
}
func (h *Hub) dashboardHandler(settings *ListenerSettings, restart func(ListenerSettings) error) http.Handler {
	mux := http.NewServeMux()
	read := func(name, ctype string) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			b, e := embeddedFiles.ReadFile(name)
			if e != nil {
				http.NotFound(w, r)
				return
			}
			w.Header().Set("Content-Type", ctype)
			_, _ = w.Write(b)
		}
	}
	mux.HandleFunc("/", read("src/AnnoPipeHub/wwwroot/index.html", "text/html; charset=utf-8"))
	mux.HandleFunc("/app.css", read("src/AnnoPipeHub/wwwroot/app.css", "text/css; charset=utf-8"))
	mux.HandleFunc("/app.js", read("src/AnnoPipeHub/wwwroot/app.js", "text/javascript; charset=utf-8"))
	mux.HandleFunc("/examples/browser-client.html", read("examples/browser-client.html", "text/html; charset=utf-8"))
	mux.HandleFunc("/api/status", func(w http.ResponseWriter, r *http.Request) {
		if !allowedDashboardHost(r) {
			http.Error(w, "forbidden", 403)
			return
		}
		writeJSON(w, map[string]any{"schemaVersion": 1, "type": "hub.status", "receivedAtUtc": time.Now().UTC(), "hub": map[string]any{"status": h.statusSnapshot(), "listener": h.listenerSnapshot()}, "clients": h.clientsSnapshot(), "fileLog": h.fileLogStatus()})
	})
	mux.HandleFunc("/api/debug", func(w http.ResponseWriter, r *http.Request) {
		if !allowedDashboardHost(r) {
			http.Error(w, "forbidden", 403)
			return
		}
		h.mu.RLock()
		entries := append([]DebugEntry(nil), h.debug...)
		h.mu.RUnlock()
		writeJSON(w, map[string]any{"entries": entries})
	})
	mux.HandleFunc("/api/settings", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == "GET" {
			if !allowedDashboardHost(r) {
				http.Error(w, "forbidden", 403)
				return
			}
			writeJSON(w, map[string]any{"settings": *settings, "interfaces": privateInterfaces()})
			return
		}
		if r.Method != "POST" || !allowedOrigin(r.Header.Get("Origin")) {
			http.Error(w, "forbidden", 403)
			return
		}
		var req ListenerSettings
		if json.NewDecoder(r.Body).Decode(&req) != nil || !validPort(req.Port) {
			writeJSONStatus(w, 400, map[string]any{"ok": false, "error": "Der Port muss zwischen 1024 und 65535 liegen."})
			return
		}
		if req.LANEnabled {
			if req.LANAddress == nil || !isPrivate(net.ParseIP(*req.LANAddress)) {
				writeJSONStatus(w, 400, map[string]any{"ok": false, "error": "Für den LAN-Modus muss eine private IPv4-Adresse ausgewählt werden."})
				return
			}
		}
		if e := restart(req); e != nil {
			writeJSONStatus(w, 400, map[string]any{"ok": false, "error": e.Error()})
			return
		}
		*settings = req
		_ = saveSettings(req)
		writeJSON(w, map[string]any{"ok": true, "listener": req})
	})
	mux.HandleFunc("/api/debug/clear", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "POST" || !allowedOrigin(r.Header.Get("Origin")) {
			http.Error(w, "forbidden", 403)
			return
		}
		h.clearDebug()
		writeJSON(w, map[string]any{"ok": true})
	})
	mux.HandleFunc("/api/file-log", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "POST" || !allowedOrigin(r.Header.Get("Origin")) {
			http.Error(w, "forbidden", 403)
			return
		}
		var req struct {
			Enabled bool `json:"enabled"`
		}
		_ = json.NewDecoder(r.Body).Decode(&req)
		var st FileLogStatus
		if req.Enabled {
			st = h.fileLog.enable()
		} else {
			st = h.fileLog.disable()
		}
		writeJSON(w, map[string]any{"ok": st.Available || !req.Enabled, "fileLog": st})
	})
	mux.HandleFunc("/api/", func(w http.ResponseWriter, r *http.Request) { http.NotFound(w, r) })
	return mux
}
func writeJSON(w http.ResponseWriter, v any) {
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(v)
}
func writeJSONStatus(w http.ResponseWriter, status int, v any) {
	w.WriteHeader(status)
	writeJSON(w, v)
}
func privateInterfaces() []map[string]string {
	out := []map[string]string{}
	ifs, _ := net.Interfaces()
	for _, i := range ifs {
		addrs, _ := i.Addrs()
		for _, a := range addrs {
			host, _, e := net.ParseCIDR(a.String())
			if e == nil && isPrivate(host) {
				out = append(out, map[string]string{"address": host.String(), "label": host.String()})
			}
		}
	}
	return out
}

func listenerBindAddress(s ListenerSettings) string {
	if s.LANEnabled {
		return fmt.Sprintf("%s:%d", *s.LANAddress, s.Port)
	}
	return fmt.Sprintf("127.0.0.1:%d", s.Port)
}

func listenerBindAddresses(s ListenerSettings) []string {
	addresses := []string{fmt.Sprintf("127.0.0.1:%d", s.Port)}
	if s.LANEnabled {
		addresses = append(addresses, listenerBindAddress(s))
	}
	return addresses
}

func runDataServer(ctx context.Context, h *Hub, s ListenerSettings) error {
	up := websocket.Upgrader{CheckOrigin: func(r *http.Request) bool { return allowedOrigin(r.Header.Get("Origin")) }, ReadBufferSize: 4096, WriteBufferSize: 4096}
	mux := http.NewServeMux()
	mux.HandleFunc("/ws", func(w http.ResponseWriter, r *http.Request) {
		if !up.CheckOrigin(r) {
			http.Error(w, "forbidden", 403)
			return
		}
		conn, e := up.Upgrade(w, r, nil)
		if e != nil {
			return
		}
		defer conn.Close()
		endpoint := r.RemoteAddr
		c := newClient(conn, endpoint)
		h.addClient(c)
		defer func() { h.removeClient(c.id); c.close() }()
		c.enqueueJSON(h.statusWire())
		c.enqueueJSON(map[string]any{"schemaVersion": 1, "type": "state.snapshot", "receivedAtUtc": time.Now().UTC(), "snapshots": h.snapshotWire()})
		go c.sendLoop()
		for {
			if _, _, e := conn.ReadMessage(); e != nil {
				return
			}
		}
	})
	servers := make([]*http.Server, 0, 2)
	listeners := make([]net.Listener, 0, 2)
	for _, addr := range listenerBindAddresses(s) {
		listener, e := net.Listen("tcp", addr)
		if e != nil {
			for _, openListener := range listeners {
				_ = openListener.Close()
			}
			return e
		}
		listeners = append(listeners, listener)
		servers = append(servers, &http.Server{Handler: mux})
	}
	addresses := strings.Join(listenerBindAddresses(s), ", ")
	h.setListener(ListenerStatus{s.LANEnabled, addresses, s.Port, "Listening", 0, nil})
	serverErrors := make(chan error, len(servers))
	for index, server := range servers {
		go func(index int, server *http.Server) {
			e := server.Serve(listeners[index])
			if e != http.ErrServerClosed {
				serverErrors <- e
			}
		}(index, server)
	}
	select {
	case <-ctx.Done():
		for _, server := range servers {
			_ = server.Shutdown(context.Background())
		}
		return nil
	case e := <-serverErrors:
		for _, server := range servers {
			_ = server.Shutdown(context.Background())
		}
		return e
	}
}
func (c *client) enqueueJSON(v any) { b, _ := json.Marshal(v); c.enqueue(string(b)) }

func runPipeReader(ctx context.Context, h *Hub) {
	delay := time.Second
	for ctx.Err() == nil {
		h.addDebug("pipe", "pipe.connecting", map[string]any{"apiOperation": "winio.DialPipeAccess"}, nil, nil, nil, nil)
		h.setPipeState(Waiting, "pipe.waiting", nil, nil)
		timeout := 2 * time.Second
		connectContext, cancel := context.WithTimeout(ctx, timeout)
		conn, e := winio.DialPipeAccess(connectContext, "\\\\.\\pipe\\anno117", uint32(windows.GENERIC_READ))
		cancel()
		if e != nil {
			failure := diagnosePipeError(e)
			errText := failure.name
			h.setPipeState(failure.state, "pipe."+strings.ToLower(string(failure.state)), &errText, map[string]any{"windowsCode": failure.code})
			h.addDebug("error", "pipe."+strings.ToLower(string(failure.state)), map[string]any{"windowsCode": failure.code, "windowsName": failure.name, "errorText": failure.errorText, "goErrorType": failure.goErrorType}, nil, nil, nil, &PipeError{Category: "pipe." + strings.ToLower(string(failure.state)), Severity: failure.severity, APIOperation: "winio.DialPipeAccess", WindowsCode: failure.code, WindowsName: failure.name, ErrorText: failure.errorText, GoErrorType: failure.goErrorType, EventKey: "pipe." + strings.ToLower(string(failure.state)), Parameters: map[string]any{"windowsCode": failure.code, "windowsName": failure.name, "errorText": failure.errorText, "goErrorType": failure.goErrorType, "apiOperation": "winio.DialPipeAccess"}})
			waitContext(ctx, delay)
			if delay < 15*time.Second {
				delay *= 2
			}
			continue
		}
		delay = time.Second
		h.setPipeState(Connected, "pipe.connected", nil, nil)
		if e = readPipe(ctx, h, conn); e != nil && ctx.Err() == nil {
			h.clearSnapshots()
			h.setPipeState(Disconnected, "pipe.disconnected", nil, nil)
		}
		_ = conn.Close()
		waitContext(ctx, time.Second)
	}
}
func readPipe(ctx context.Context, h *Hub, r io.Reader) error {
	head := make([]byte, 4)
	if _, e := io.ReadFull(r, head); e != nil {
		return e
	}
	n := int(int32(binary.LittleEndian.Uint32(head)))
	if n < 1 || n > maxFrameSize {
		return fmt.Errorf("Ungültige Nachrichtenlänge: %d.", n)
	}
	frame := make([]byte, n)
	if _, e := io.ReadFull(r, frame); e != nil {
		return e
	}
	m, e := decodeFrame(frame, true)
	if e != nil {
		return e
	}
	if m.Version == nil || *m.Version != protocolVersion {
		return fmt.Errorf("Nicht unterstützte Protokollversion")
	}
	v := *m.Version
	h.setProtocol(v)
	for ctx.Err() == nil {
		if _, e := io.ReadFull(r, head); e != nil {
			return e
		}
		n = int(int32(binary.LittleEndian.Uint32(head)))
		if n < 1 || n > maxFrameSize {
			return fmt.Errorf("Ungültige Nachrichtenlänge: %d.", n)
		}
		frame = make([]byte, n)
		if _, e := io.ReadFull(r, frame); e != nil {
			return e
		}
		m, e = decodeFrame(frame, false)
		if e != nil {
			return e
		}
		h.accept(m, n, frame)
	}
	return ctx.Err()
}
func waitContext(ctx context.Context, d time.Duration) {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
	case <-t.C:
	}
}

type pipeFailure struct {
	state       PipeState
	name        string
	code        int
	severity    string
	errorText   string
	goErrorType string
}

func diagnosePipeError(err error) pipeFailure {
	failure := pipeFailure{state: Disconnected, name: "UNKNOWN", severity: "ERROR", errorText: err.Error(), goErrorType: fmt.Sprintf("%T", err)}
	if errors.Is(err, winio.ErrTimeout) || errors.Is(err, context.DeadlineExceeded) {
		failure.state = Timeout
		failure.name = "ERROR_TIMEOUT"
		failure.code = int(windows.ERROR_TIMEOUT)
		failure.severity = "WARN"
		return failure
	}
	var errno windows.Errno
	if errors.As(err, &errno) {
		failure.code = int(errno)
		failure.name = windowsErrorName(errno)
		switch errno {
		case windows.ERROR_FILE_NOT_FOUND:
			failure.state, failure.severity = NotFound, "WARN"
		case windows.ERROR_PIPE_BUSY:
			failure.state, failure.severity = Busy, "WARN"
		case windows.ERROR_ACCESS_DENIED:
			failure.state = Disconnected
		case windows.ERROR_TIMEOUT:
			failure.state, failure.severity = Timeout, "WARN"
		case windows.ERROR_BROKEN_PIPE, windows.ERROR_PIPE_NOT_CONNECTED:
			failure.state = Disconnected
		}
	}
	return failure
}

func classifyPipeError(err error) (PipeState, string, int, string) {
	failure := diagnosePipeError(err)
	return failure.state, failure.name, failure.code, failure.severity
}

func windowsErrorName(errno windows.Errno) string {
	switch errno {
	case windows.ERROR_FILE_NOT_FOUND:
		return "ERROR_FILE_NOT_FOUND"
	case windows.ERROR_PIPE_BUSY:
		return "ERROR_PIPE_BUSY"
	case windows.ERROR_ACCESS_DENIED:
		return "ERROR_ACCESS_DENIED"
	case windows.ERROR_TIMEOUT:
		return "ERROR_TIMEOUT"
	case windows.ERROR_BROKEN_PIPE:
		return "ERROR_BROKEN_PIPE"
	case windows.ERROR_PIPE_NOT_CONNECTED:
		return "ERROR_PIPE_NOT_CONNECTED"
	default:
		return "UNKNOWN"
	}
}

// File logging is deliberately opt-in.
type FileLogger struct {
	hub       *Hub
	mu        sync.Mutex
	enabled   bool
	available bool
	path      string
	err       string
	file      *os.File
	queue     chan DebugEntry
	done      chan struct{}
	raw       bool
}

func newFileLogger(h *Hub) *FileLogger {
	f := &FileLogger{hub: h, queue: make(chan DebugEntry, 512), done: make(chan struct{}), raw: os.Getenv("ANNO117PIPEHUB_RAW_LOG") == "1"}
	go f.loop()
	return f
}
func (f *FileLogger) status() FileLogStatus {
	f.mu.Lock()
	defer f.mu.Unlock()
	var p, e *string
	if f.path != "" {
		p = &f.path
	}
	if f.err != "" {
		e = &f.err
	}
	return FileLogStatus{f.enabled, f.available, p, e}
}
func (h *Hub) fileLogStatus() FileLogStatus {
	h.mu.RLock()
	f := h.fileLog
	h.mu.RUnlock()
	if f == nil {
		return FileLogStatus{}
	}
	return f.status()
}
func (f *FileLogger) enable() FileLogStatus {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.enabled {
		return f.statusLocked()
	}
	if e := os.MkdirAll(filepath.Dir(f.defaultPath()), 0755); e != nil {
		return f.failLocked(e)
	}
	if e := f.openLocked(time.Now()); e != nil {
		return f.failLocked(e)
	}
	f.enabled = true
	f.available = true
	f.err = ""
	return f.statusLocked()
}
func (f *FileLogger) disable() FileLogStatus {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.enabled = false
	f.available = false
	f.err = ""
	if f.file != nil {
		_ = f.file.Sync()
		_ = f.file.Close()
		f.file = nil
	}
	return f.statusLocked()
}
func (f *FileLogger) statusLocked() FileLogStatus {
	var p, e *string
	if f.path != "" {
		p = &f.path
	}
	if f.err != "" {
		e = &f.err
	}
	return FileLogStatus{f.enabled, f.available, p, e}
}
func (f *FileLogger) failLocked(e error) FileLogStatus {
	f.enabled = false
	f.available = false
	f.err = "filelog.unavailable"
	return f.statusLocked()
}
func (f *FileLogger) defaultPath() string {
	return filepath.Join(executableDir(), fmt.Sprintf("Anno117PipeHub-%s.log", time.Now().Format("2006-01-02")))
}
func (f *FileLogger) openLocked(t time.Time) error {
	if f.file != nil {
		_ = f.file.Close()
	}
	base := filepath.Join(executableDir(), fmt.Sprintf("Anno117PipeHub-%s.log", t.Format("2006-01-02")))
	path := base
	for i := 1; ; i++ {
		st, e := os.Stat(path)
		if os.IsNotExist(e) || st.Size() < 5*1024*1024 {
			break
		}
		path = filepath.Join(executableDir(), fmt.Sprintf("Anno117PipeHub-%s-%02d.log", t.Format("2006-01-02"), i))
	}
	file, e := os.OpenFile(path, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0600)
	if e != nil {
		return e
	}
	f.file = file
	f.path = path
	f.cleanup()
	return nil
}
func (f *FileLogger) cleanup() {
	matches, _ := filepath.Glob(filepath.Join(executableDir(), "Anno117PipeHub-*.log"))
	sort.Slice(matches, func(i, j int) bool {
		ai, _ := os.Stat(matches[i])
		aj, _ := os.Stat(matches[j])
		return ai.ModTime().After(aj.ModTime())
	})
	for _, p := range matches[7:] {
		_ = os.Remove(p)
	}
}
func (f *FileLogger) enqueue(e DebugEntry) {
	f.mu.Lock()
	on := f.enabled
	f.mu.Unlock()
	if !on {
		return
	}
	select {
	case f.queue <- e:
	default:
		select {
		case <-f.queue:
		default:
		}
		select {
		case f.queue <- e:
		default:
		}
	}
}
func (f *FileLogger) loop() {
	for e := range f.queue {
		f.mu.Lock()
		if f.enabled && f.file != nil {
			if st, err := f.file.Stat(); err != nil || st.Size() >= 5*1024*1024 {
				_ = f.openLocked(e.AtUTC)
			}
			line, _ := json.Marshal(e)
			_, err := fmt.Fprintln(f.file, string(line))
			if err != nil {
				_ = f.failLocked(err)
			}
			_ = f.file.Sync()
		}
		f.mu.Unlock()
	}
}
func executableDir() string {
	exe, e := os.Executable()
	if e != nil {
		return "."
	}
	return filepath.Dir(exe)
}

func main() {
	log.SetFlags(log.LstdFlags | log.Lmicroseconds)
	h := newHub()
	fl := newFileLogger(h)
	h.fileLog = fl
	settings := loadSettings()
	if !validPort(settings.Port) {
		settings.Port = defaultDataPort
	}
	_ = saveSettings(settings)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go runPipeReader(ctx, h)
	var dataCancel context.CancelFunc
	restart := func(s ListenerSettings) error {
		if s.LANEnabled && (s.LANAddress == nil || !isPrivate(net.ParseIP(*s.LANAddress))) {
			return errors.New("Für den LAN-Modus muss eine private IPv4-Adresse ausgewählt werden.")
		}
		if dataCancel != nil {
			dataCancel()
		}
		var c context.Context
		c, dataCancel = context.WithCancel(ctx)
		go func() {
			if e := runDataServer(c, h, s); e != nil {
				log.Printf("Daten-Listener: %v", e)
			}
		}()
		return nil
	}
	_ = restart(settings)
	dashboard := &http.Server{Addr: dashboardAddr, Handler: h.dashboardHandler(&settings, restart)}
	log.Printf("Dashboard: http://%s/", dashboardAddr)
	if e := dashboard.ListenAndServe(); e != nil && !errors.Is(e, http.ErrServerClosed) {
		log.Fatal(e)
	}
}
