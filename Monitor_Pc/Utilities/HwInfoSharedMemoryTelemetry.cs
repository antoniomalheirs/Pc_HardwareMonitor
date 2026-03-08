using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;

namespace Monitor_Pc.Utilities
{
    public sealed class HwInfoSharedMemoryTelemetry : IDisposable
    {
        private const string HwInfoSharedMemoryName = "Global\\HWiNFO_SENS_SM2";
        private MemoryMappedFile? _memoryMappedFile;

        public bool TryReadSnapshot(out IReadOnlyList<HwInfoReading> readings)
        {
            readings = Array.Empty<HwInfoReading>();

            try
            {
                _memoryMappedFile ??= MemoryMappedFile.OpenExisting(HwInfoSharedMemoryName, MemoryMappedFileRights.Read);

                using var stream = _memoryMappedFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
                using var reader = new BinaryReader(stream);

                var header = ReadHeader(reader);
                if (!header.IsSignatureValid || header.ReadingElementCount <= 0)
                {
                    return false;
                }

                var sensorsByIndex = ReadSensors(reader, header);
                readings = ReadReadings(reader, header, sensorsByIndex);
                return readings.Count > 0;
            }
            catch
            {
                readings = Array.Empty<HwInfoReading>();
                return false;
            }
        }

        private static HwInfoHeader ReadHeader(BinaryReader reader)
        {
            reader.BaseStream.Position = 0;

            var headerBytes = reader.ReadBytes(Marshal.SizeOf<HwInfoHeader>());
            if (headerBytes.Length < Marshal.SizeOf<HwInfoHeader>())
            {
                return default;
            }

            return ByteArrayToStructure<HwInfoHeader>(headerBytes);
        }

        private static Dictionary<uint, string> ReadSensors(BinaryReader reader, HwInfoHeader header)
        {
            var sensors = new Dictionary<uint, string>();
            var sensorStructSize = Marshal.SizeOf<HwInfoSensorElement>();

            for (uint i = 0; i < header.SensorElementCount; i++)
            {
                var offset = header.SensorOffset + (i * header.SensorElementSize);
                reader.BaseStream.Position = offset;

                var bytes = reader.ReadBytes((int)Math.Max(header.SensorElementSize, (uint)sensorStructSize));
                if (bytes.Length < sensorStructSize)
                {
                    continue;
                }

                var sensor = ByteArrayToStructure<HwInfoSensorElement>(bytes);
                var sensorName = string.IsNullOrWhiteSpace(sensor.SensorNameUser)
                    ? sensor.SensorNameOrig
                    : sensor.SensorNameUser;

                if (!string.IsNullOrWhiteSpace(sensorName))
                {
                    sensors[i] = sensorName.Trim();
                }
            }

            return sensors;
        }

        private static IReadOnlyList<HwInfoReading> ReadReadings(BinaryReader reader, HwInfoHeader header, IReadOnlyDictionary<uint, string> sensors)
        {
            var items = new List<HwInfoReading>();
            var readingStructSize = Marshal.SizeOf<HwInfoReadingElement>();

            for (uint i = 0; i < header.ReadingElementCount; i++)
            {
                var offset = header.ReadingOffset + (i * header.ReadingElementSize);
                reader.BaseStream.Position = offset;

                var bytes = reader.ReadBytes((int)Math.Max(header.ReadingElementSize, (uint)readingStructSize));
                if (bytes.Length < readingStructSize)
                {
                    continue;
                }

                var reading = ByteArrayToStructure<HwInfoReadingElement>(bytes);
                var label = string.IsNullOrWhiteSpace(reading.LabelUser)
                    ? reading.LabelOrig
                    : reading.LabelUser;

                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                sensors.TryGetValue(reading.SensorIndex, out var sensorName);

                items.Add(new HwInfoReading(
                    sensorName ?? string.Empty,
                    label.Trim(),
                    reading.Unit.Trim(),
                    (float)reading.Value,
                    (float)reading.ValueMin,
                    (float)reading.ValueMax,
                    (float)reading.ValueAvg));
            }

            return items;
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            _memoryMappedFile?.Dispose();
            _memoryMappedFile = null;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HwInfoHeader
        {
            public uint Signature;
            public uint Version;
            public uint Revision;
            public ulong PollTime;
            public uint SensorOffset;
            public uint SensorElementSize;
            public uint SensorElementCount;
            public uint ReadingOffset;
            public uint ReadingElementSize;
            public uint ReadingElementCount;

            public bool IsSignatureValid => Signature == 0x53695748; // "HWiS"
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        private struct HwInfoSensorElement
        {
            public uint SensorId;
            public uint SensorInstance;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string SensorNameOrig;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string SensorNameUser;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        private struct HwInfoReadingElement
        {
            public uint ReadingType;
            public uint SensorIndex;
            public uint ReadingId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string LabelOrig;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string LabelUser;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Unit;

            public double Value;
            public double ValueMin;
            public double ValueMax;
            public double ValueAvg;
        }
    }

    public sealed record HwInfoReading(
        string SensorName,
        string Label,
        string Unit,
        float Value,
        float Minimum,
        float Maximum,
        float Average)
    {
        public string CombinedName => string.IsNullOrWhiteSpace(SensorName)
            ? Label
            : $"{SensorName} - {Label}";

        public bool HasKeywords(params string[] keywords)
        {
            return keywords.Any(keyword =>
                Label.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                SensorName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}
