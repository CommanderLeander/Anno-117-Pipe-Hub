using System.Buffers.Binary;

namespace AnnoPipeHub;

public enum PipeMessageType : byte
{
    Version = 0,
    SessionStart = 1,
    SessionEnd = 2,
    AreaProductionStatistics = 3
}

public sealed class ProtocolException(string message) : Exception(message);

public sealed record ProductionEntry(
    int ProductGuid,
    float ProductGeneration,
    float ProductConsumption,
    float ProductDelta,
    float PerfectProductGeneration,
    float PerfectProductConsumption,
    int AmountOfBuildings,
    int TotalMaintenance,
    float TotalIncome,
    int TotalProfit,
    float SummedProductivity,
    float AverageProductivity,
    IReadOnlyDictionary<int, int> WorkforceGuidToAmount,
    IReadOnlyDictionary<int, int> BuildingGuidToAmount);

public sealed record SessionStartData(string SessionName);

public sealed record AreaProductionStatisticsData(
    byte SessionId,
    byte IslandId,
    byte AreaIndex,
    int SessionGuid,
    string AreaName,
    long RawTimestamp,
    IReadOnlyList<ProductionEntry> Entries);

public sealed record DecodedPipeMessage(
    PipeMessageType Type,
    int? Version = null,
    SessionStartData? SessionStart = null,
    AreaProductionStatisticsData? Statistics = null);

public static class PipeDecoder
{
    public const int ProtocolVersion = 2;
    public const int MaxMessageSize = 1024 * 1024;
    private const int MaxCollectionCount = MaxMessageSize / 8;

    public static DecodedPipeMessage DecodeFrame(ReadOnlySpan<byte> frame, bool isPreamble = false)
    {
        if (frame.Length == 0)
            throw new ProtocolException("Nachricht enthält keine Nutzdaten.");

        var reader = new FrameReader(frame);
        var type = (PipeMessageType)reader.ReadByte("Nachrichtentyp");
        if (!Enum.IsDefined(type))
            throw new ProtocolException($"Unbekannter Nachrichtentyp: {(byte)type}.");

        if (isPreamble && type != PipeMessageType.Version)
            throw new ProtocolException("Die erste Pipe-Nachricht muss die Version enthalten.");

        return type switch
        {
            PipeMessageType.Version => DecodeVersion(reader),
            PipeMessageType.SessionStart => new DecodedPipeMessage(type, SessionStart: new SessionStartData(reader.ReadString())),
            PipeMessageType.SessionEnd => DecodeEnd(reader),
            PipeMessageType.AreaProductionStatistics => DecodeStatistics(reader),
            _ => throw new ProtocolException("Unbekannter Nachrichtentyp.")
        };
    }

    public static byte[] CreateFrame(ReadOnlySpan<byte> payload)
    {
        ValidateMessageLength(payload.Length);
        var result = new byte[payload.Length + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(result, payload.Length);
        payload.CopyTo(result.AsSpan(sizeof(int)));
        return result;
    }

    public static void ValidateMessageLength(int length)
    {
        if (length < 1 || length > MaxMessageSize)
            throw new ProtocolException($"Ungültige Nachrichtenlänge: {length}.");
    }

    private static DecodedPipeMessage DecodeVersion(FrameReader reader)
    {
        var version = reader.ReadInt32("Protokollversion");
        reader.EnsureEnd();
        return new DecodedPipeMessage(PipeMessageType.Version, version);
    }

    private static DecodedPipeMessage DecodeEnd(FrameReader reader)
    {
        reader.EnsureEnd();
        return new DecodedPipeMessage(PipeMessageType.SessionEnd);
    }

    private static DecodedPipeMessage DecodeStatistics(FrameReader reader)
    {
        var sessionId = reader.ReadByte("Session-ID");
        var islandId = reader.ReadByte("Insel-ID");
        var areaIndex = reader.ReadByte("Area-Index");
        var sessionGuid = reader.ReadInt32("Session-GUID");
        var areaName = reader.ReadString();
        var rawTimestamp = reader.ReadInt64("Zeitstempel");
        var entryCount = reader.ReadCount("Produktionsanzahl");
        var entries = new List<ProductionEntry>(entryCount);
        for (var index = 0; index < entryCount; index++)
            entries.Add(ReadProductionEntry(ref reader));
        reader.EnsureEnd();
        return new DecodedPipeMessage(
            PipeMessageType.AreaProductionStatistics,
            Statistics: new AreaProductionStatisticsData(sessionId, islandId, areaIndex, sessionGuid, areaName, rawTimestamp, entries));
    }

    private static ProductionEntry ReadProductionEntry(ref FrameReader reader)
    {
        var productGuid = reader.ReadInt32("Produkt-GUID");
        var generation = reader.ReadSingle("Erzeugung");
        var consumption = reader.ReadSingle("Verbrauch");
        var delta = reader.ReadSingle("Delta");
        var perfectGeneration = reader.ReadSingle("perfekte Erzeugung");
        var perfectConsumption = reader.ReadSingle("perfekter Verbrauch");
        var buildingCount = reader.ReadInt32("Gebäudeanzahl");
        var maintenance = reader.ReadInt32("Wartung");
        var income = reader.ReadSingle("Einkommen");
        var profit = reader.ReadInt32("Gewinn");
        var summedProductivity = reader.ReadSingle("SummedProductivity");
        var averageProductivity = reader.ReadSingle("AverageProductivity");
        var workforce = reader.ReadMap("Workforce-GUID-Menge");
        var buildings = reader.ReadMap("Building-GUID-Menge");
        return new ProductionEntry(productGuid, generation, consumption, delta, perfectGeneration, perfectConsumption,
            buildingCount, maintenance, income, profit, summedProductivity, averageProductivity, workforce, buildings);
    }

    private ref struct FrameReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public byte ReadByte(string field)
        {
            EnsureAvailable(1, field);
            return _data[_position++];
        }

        public int ReadInt32(string field)
        {
            EnsureAvailable(sizeof(int), field);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data[_position..]);
            _position += sizeof(int);
            return value;
        }

        public long ReadInt64(string field)
        {
            EnsureAvailable(sizeof(long), field);
            var value = BinaryPrimitives.ReadInt64LittleEndian(_data[_position..]);
            _position += sizeof(long);
            return value;
        }

        public float ReadSingle(string field)
        {
            EnsureAvailable(sizeof(float), field);
            var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_data[_position..]));
            _position += sizeof(float);
            return value;
        }

        public string ReadString()
        {
            var length = ReadByte("String-Länge");
            EnsureAvailable(length, "String-Inhalt");
            var value = System.Text.Encoding.UTF8.GetString(_data.Slice(_position, length));
            _position += length;
            return value;
        }

        public int ReadCount(string field)
        {
            var count = ReadInt32(field);
            if (count < 0 || count > MaxCollectionCount)
                throw new ProtocolException($"Ungültige Anzahl für {field}: {count}.");
            return count;
        }

        public IReadOnlyDictionary<int, int> ReadMap(string field)
        {
            var count = ReadCount(field);
            var values = new Dictionary<int, int>(count);
            for (var index = 0; index < count; index++)
            {
                var guid = ReadInt32($"{field}-GUID");
                var amount = ReadInt32($"{field}-Menge");
                values[guid] = values.TryGetValue(guid, out var existing) ? existing + amount : amount;
            }
            return values;
        }

        public void EnsureEnd()
        {
            if (_position != _data.Length)
                throw new ProtocolException($"Nachricht enthält {_data.Length - _position} unerwartete Bytes.");
        }

        private void EnsureAvailable(int count, string field)
        {
            if (count < 0 || _position > _data.Length - count)
                throw new ProtocolException($"Nachricht ist bei {field} abgeschnitten.");
        }
    }
}