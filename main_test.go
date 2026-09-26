package main

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"net"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/Microsoft/go-winio"
	"golang.org/x/sys/windows"
)

func TestDecodeVersionPreamble(t *testing.T) {
	frame := make([]byte, 5)
	frame[0] = 0
	binary.LittleEndian.PutUint32(frame[1:], protocolVersion)
	message, err := decodeFrame(frame, true)
	if err != nil {
		t.Fatal(err)
	}
	if message.Version == nil || *message.Version != protocolVersion {
		t.Fatalf("version = %v, want %d", message.Version, protocolVersion)
	}
}

func TestDecodeStatisticsAndDuplicateMapKeys(t *testing.T) {
	payload := make([]byte, 0, 128)
	payload = append(payload, 3, 1, 2, 3)
	appendInt32 := func(value int32) {
		var bytes [4]byte
		binary.LittleEndian.PutUint32(bytes[:], uint32(value))
		payload = append(payload, bytes[:]...)
	}
	appendInt64 := func(value int64) {
		var bytes [8]byte
		binary.LittleEndian.PutUint64(bytes[:], uint64(value))
		payload = append(payload, bytes[:]...)
	}
	appendFloat32 := func(value float32) { appendInt32(int32(math.Float32bits(value))) }
	appendString := func(value string) { payload = append(payload, byte(len(value))); payload = append(payload, value...) }
	appendInt32(42)
	appendString("Island")
	appendInt64(123)
	appendInt32(1)
	appendInt32(7)
	appendFloat32(1.5)
	appendFloat32(2.5)
	appendFloat32(-1)
	appendFloat32(3)
	appendFloat32(4)
	appendInt32(5)
	appendInt32(6)
	appendFloat32(7)
	appendInt32(8)
	appendFloat32(9)
	appendFloat32(10)
	appendInt32(2)
	appendInt32(99)
	appendInt32(4)
	appendInt32(99)
	appendInt32(-1)
	appendInt32(1)
	appendInt32(88)
	appendInt32(2)

	message, err := decodeFrame(payload, false)
	if err != nil {
		t.Fatal(err)
	}
	if message.Statistics == nil || len(message.Statistics.Entries) != 1 {
		t.Fatalf("statistics = %#v", message.Statistics)
	}
	entry := message.Statistics.Entries[0]
	if entry.WorkforceGUIDToAmount[99] != 3 || entry.BuildingGUIDToAmount[88] != 2 {
		t.Fatalf("maps = %#v, %#v", entry.WorkforceGUIDToAmount, entry.BuildingGUIDToAmount)
	}
}

func TestStatisticsFrameUpdatesSnapshotAndWireEvent(t *testing.T) {
	payload := make([]byte, 0, 64)
	payload = append(payload, 3, 1, 2, 3)
	appendInt32 := func(value int32) {
		var bytes [4]byte
		binary.LittleEndian.PutUint32(bytes[:], uint32(value))
		payload = append(payload, bytes[:]...)
	}
	appendInt64 := func(value int64) {
		var bytes [8]byte
		binary.LittleEndian.PutUint64(bytes[:], uint64(value))
		payload = append(payload, bytes[:]...)
	}
	appendInt32(42)
	payload = append(payload, byte(len("Island")))
	payload = append(payload, "Island"...)
	appendInt64(123)
	appendInt32(0)

	message, err := decodeFrame(payload, false)
	if err != nil || message.Statistics == nil {
		t.Fatalf("decode statistics: message=%#v error=%v", message, err)
	}
	wire, err := json.Marshal(wireMessage(message))
	if err != nil {
		t.Fatal(err)
	}
	var event map[string]any
	if err := json.Unmarshal(wire, &event); err != nil {
		t.Fatal(err)
	}
	if event["type"] != "area.production.statistics" || event["schemaVersion"] != float64(1) {
		t.Fatalf("wire event = %#v", event)
	}
	statistics, ok := event["statistics"].(map[string]any)
	if !ok || statistics["areaName"] != "Island" || statistics["sessionGuid"] != float64(42) {
		t.Fatalf("wire statistics = %#v", event["statistics"])
	}

	hub := newHub()
	hub.accept(message, len(payload), payload)
	snapshots := hub.snapshotWire()
	if len(snapshots) != 1 {
		t.Fatalf("snapshot count = %d, want 1", len(snapshots))
	}
	snapshotWire, err := json.Marshal(map[string]any{
		"schemaVersion": 1,
		"type":          "state.snapshot",
		"receivedAtUtc": time.Now().UTC(),
		"snapshots":     snapshots,
	})
	if err != nil {
		t.Fatal(err)
	}
	var snapshot map[string]any
	if err := json.Unmarshal(snapshotWire, &snapshot); err != nil {
		t.Fatal(err)
	}
	if snapshot["type"] != "state.snapshot" || len(snapshot["snapshots"].([]any)) != 1 {
		t.Fatalf("state snapshot = %#v", snapshot)
	}
}

func TestDecodeRejectsTrailingBytesAndWrongPreamble(t *testing.T) {
	if _, err := decodeFrame([]byte{1, 0}, true); err == nil {
		t.Fatal("expected wrong preamble error")
	}
	if _, err := decodeFrame([]byte{2, 0}, false); err == nil {
		t.Fatal("expected trailing byte error")
	}
}

func TestOriginAndHostRules(t *testing.T) {
	for _, origin := range []string{"", "http://127.0.0.1:8765"} {
		if !allowedOrigin(origin) {
			t.Errorf("origin %q should be accepted", origin)
		}
	}
	for _, origin := range []string{"null", "http://127.0.0.1:8765/", "http://localhost:8765", "http://127.0.0.1:8766", "https://127.0.0.1:8765"} {
		if allowedOrigin(origin) {
			t.Errorf("origin %q should be rejected", origin)
		}
	}
	for _, host := range []string{"127.0.0.1:8765"} {
		if !allowedDashboardHost(httptest.NewRequest("GET", "http://"+host+"/api/settings", nil)) {
			t.Errorf("host %q should be accepted", host)
		}
	}
	for _, host := range []string{"localhost:8765", "[::1]:8765", "192.168.0.45:8765"} {
		if allowedDashboardHost(httptest.NewRequest("GET", "http://"+host+"/api/settings", nil)) {
			t.Errorf("host %q should be rejected", host)
		}
	}
}

func TestListenerBindAddresses(t *testing.T) {
	if got := listenerBindAddress(ListenerSettings{Port: 8766}); got != "127.0.0.1:8766" {
		t.Fatalf("loopback bind address = %q", got)
	}
	addresses := listenerBindAddresses(ListenerSettings{Port: 8766})
	if len(addresses) != 1 || addresses[0] != "127.0.0.1:8766" {
		t.Fatalf("loopback listener addresses = %#v", addresses)
	}
	lanAddress := "192.168.0.45"
	settings := ListenerSettings{Port: 8766, LANEnabled: true, LANAddress: &lanAddress}
	if got := listenerBindAddress(settings); got != "192.168.0.45:8766" {
		t.Fatalf("LAN bind address = %q", got)
	}
	addresses = listenerBindAddresses(settings)
	if len(addresses) != 2 || addresses[0] != "127.0.0.1:8766" || addresses[1] != "192.168.0.45:8766" {
		t.Fatalf("LAN listener addresses = %#v", addresses)
	}
}

func TestDataServerBindsLoopbackAndLAN(t *testing.T) {
	probe, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := probe.Addr().(*net.TCPAddr).Port
	_ = probe.Close()
	lanAddress := "127.0.0.2"
	settings := ListenerSettings{Port: port, LANEnabled: true, LANAddress: &lanAddress}
	hub := newHub()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	serverErrors := make(chan error, 1)
	go func() { serverErrors <- runDataServer(ctx, hub, settings) }()

	for _, address := range []string{listenerBindAddress(ListenerSettings{Port: port}), fmt.Sprintf("%s:%d", lanAddress, port)} {
		deadline := time.Now().Add(time.Second)
		for {
			connection, dialError := net.DialTimeout("tcp", address, 50*time.Millisecond)
			if dialError == nil {
				_ = connection.Close()
				break
			}
			if time.Now().After(deadline) {
				t.Fatalf("listener %s did not open: %v", address, dialError)
			}
			time.Sleep(10 * time.Millisecond)
		}
	}
	cancel()
	select {
	case err := <-serverErrors:
		if err != nil {
			t.Fatalf("server shutdown: %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("server did not shut down")
	}
}

func TestDiagnoseWrappedWindowsError(t *testing.T) {
	err := fmt.Errorf("dial failed: %w", windows.ERROR_PIPE_BUSY)
	failure := diagnosePipeError(err)
	if failure.state != Busy || failure.code != int(windows.ERROR_PIPE_BUSY) || failure.name != "ERROR_PIPE_BUSY" {
		t.Fatalf("failure = %#v", failure)
	}
	if failure.errorText != err.Error() || failure.goErrorType != "*fmt.wrapError" {
		t.Fatalf("diagnostic details = %#v", failure)
	}
}

func TestDiagnoseWinioTimeout(t *testing.T) {
	err := fmt.Errorf("dial failed: %w", winio.ErrTimeout)
	failure := diagnosePipeError(err)
	if !errors.Is(err, winio.ErrTimeout) {
		t.Fatal("test error is not a wrapped winio timeout")
	}
	if failure.state != Timeout || failure.code != int(windows.ERROR_TIMEOUT) || failure.name != "ERROR_TIMEOUT" {
		t.Fatalf("failure = %#v", failure)
	}
	if failure.code == 0 || failure.name == "UNKNOWN" || failure.errorText != err.Error() {
		t.Fatalf("timeout diagnostic details = %#v", failure)
	}
}

func TestDiagnoseContextDeadline(t *testing.T) {
	err := fmt.Errorf("dial context expired: %w", context.DeadlineExceeded)
	failure := diagnosePipeError(err)
	if failure.state != Timeout || failure.code != int(windows.ERROR_TIMEOUT) || failure.name != "ERROR_TIMEOUT" {
		t.Fatalf("failure = %#v", failure)
	}
}
