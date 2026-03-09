using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace Monitor_Pc.Utilities
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  HWiNFO64 Shared Memory Reader
    //  Implements the official HWiNFO Shared Memory Interface (SMI) v2
    //  Documentation: https://www.hwinfo.com/forum/threads/shared-memory-interface.12060/
    //
    //  REQUIREMENTS:
    //    HWiNFO64 must be running with "Shared Memory Support" enabled:
    //    Settings → General → Shared Memory Support = ON
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Official struct layouts from HWiNFO SDK ───────────────────────────────

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HWiNFO_SENSORS_SHARED_MEM2
    {
        public uint    dwSignature;         // 'HWiS' = 0x53694857
        public uint    dwVersion;           // 1 or 2
        public uint    dwRevision;
        public long    poll_time;           // last poll time (FILETIME)

        public uint    dwOffsetOfSensorSection;     // offset to sensor section
        public uint    dwSizeOfSensorElement;       // size of each sensor struct
        public uint    dwNumSensorElements;         // number of sensor structs

        public uint    dwOffsetOfReadingSection;    // offset to readings section
        public uint    dwSizeOfReadingElement;      // size of each reading struct
        public uint    dwNumReadingElements;        // number of reading structs
    }

    public enum SENSOR_READING_TYPE : uint
    {
        SENSOR_TYPE_NONE   = 0,
        SENSOR_TYPE_TEMP   = 1,
        SENSOR_TYPE_VOLT   = 2,
        SENSOR_TYPE_FAN    = 3,
        SENSOR_TYPE_POWER  = 4,
        SENSOR_TYPE_CLOCK  = 5,
        SENSOR_TYPE_USAGE  = 6,
        SENSOR_TYPE_OTHER  = 7,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct HWiNFO_SENSORS_SENSOR_ELEMENT
    {
        public uint   dwSensorID;
        public uint   dwSensorInst;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szSensorNameOrig;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szSensorNameUser;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct HWiNFO_SENSORS_READING_ELEMENT
    {
        public SENSOR_READING_TYPE tReading;  // type of reading

        public uint   dwSensorIndex;   // index into sensor array (which sensor this belongs to)
        public uint   dwReadingID;     // unique reading ID

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szLabelOrig;     // original label

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szLabelUser;     // user-defined label

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string szUnit;          // unit string

        public double Value;           // current value
        public double ValueMin;
        public double ValueMax;
        public double ValueAvg;
    }

    // ── Parsed / friendly reading ─────────────────────────────────────────────

    public class HWiNFO_Reading
    {
        public SENSOR_READING_TYPE Type       { get; init; }
        public uint                SensorIndex{ get; init; }
        public uint                ReadingId  { get; init; }
        public string              SensorName { get; init; } = "";
        public string              Label      { get; init; } = "";
        public string              Unit       { get; init; } = "";
        public double              Value      { get; set;  }
        public double              Min        { get; set;  }
        public double              Max        { get; set;  }
        public double              Avg        { get; set;  }

        public override string ToString() =>
            $"[{Type}] {SensorName} / {Label} = {Value:F3} {Unit}";
    }

    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class HWiNFOReader : IDisposable
    {
        private const string  SHM_NAME  = "Global\\HWiNFO_SENS_SM2";
        private const uint    SIGNATURE = 0x53694857; // 'HWiS'

        private MemoryMappedFile?       _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private bool                    _open;
        private bool                    _disposed;

        // Last read snapshot
        private List<HWiNFO_Reading>    _readings  = new();
        private List<string>            _sensorNames = new();

        public bool IsAvailable => _open;

        // ── Open / close ──────────────────────────────────────────────────────

        public bool TryOpen()
        {
            if (_open) return true;
            try
            {
                _mmf      = MemoryMappedFile.OpenExisting(SHM_NAME, MemoryMappedFileRights.Read);
                _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _open     = true;
                return true;
            }
            catch
            {
                _open = false;
                return false;
            }
        }

        public void Close()
        {
            _accessor?.Dispose(); _accessor = null;
            _mmf?.Dispose();      _mmf = null;
            _open = false;
        }

        // ── Read all sensors ──────────────────────────────────────────────────

        public bool Refresh()
        {
            if (!_open && !TryOpen()) return false;

            try
            {
                // Read header
                _accessor!.Read(0, out HWiNFO_SENSORS_SHARED_MEM2 hdr);

                if (hdr.dwSignature != SIGNATURE)
                {
                    // HWiNFO not running or SM not enabled
                    _open = false;
                    return false;
                }

                // ── Read sensor names ─────────────────────────────────────────
                var sensorNames = new List<string>((int)hdr.dwNumSensorElements);
                long sensorBase = hdr.dwOffsetOfSensorSection;

                for (uint i = 0; i < hdr.dwNumSensorElements; i++)
                {
                    long offset = sensorBase + i * hdr.dwSizeOfSensorElement;
                    var  sensor = ReadStruct<HWiNFO_SENSORS_SENSOR_ELEMENT>(_accessor, offset);
                    // Prefer user-renamed name, fall back to original
                    string name = !string.IsNullOrWhiteSpace(sensor.szSensorNameUser)
                        ? sensor.szSensorNameUser
                        : sensor.szSensorNameOrig;
                    sensorNames.Add(name ?? $"Sensor_{i}");
                }

                // ── Read individual readings ──────────────────────────────────
                var readings = new List<HWiNFO_Reading>((int)hdr.dwNumReadingElements);
                long readBase = hdr.dwOffsetOfReadingSection;

                for (uint i = 0; i < hdr.dwNumReadingElements; i++)
                {
                    long offset  = readBase + i * hdr.dwSizeOfReadingElement;
                    var  element = ReadStruct<HWiNFO_SENSORS_READING_ELEMENT>(_accessor, offset);

                    string sensorName = element.dwSensorIndex < sensorNames.Count
                        ? sensorNames[(int)element.dwSensorIndex]
                        : $"Sensor_{element.dwSensorIndex}";

                    string label = !string.IsNullOrWhiteSpace(element.szLabelUser)
                        ? element.szLabelUser
                        : element.szLabelOrig;

                    readings.Add(new HWiNFO_Reading
                    {
                        Type        = element.tReading,
                        SensorIndex = element.dwSensorIndex,
                        ReadingId   = element.dwReadingID,
                        SensorName  = sensorName,
                        Label       = label ?? "",
                        Unit        = element.szUnit ?? "",
                        Value       = element.Value,
                        Min         = element.ValueMin,
                        Max         = element.ValueMax,
                        Avg         = element.ValueAvg,
                    });
                }

                _readings     = readings;
                _sensorNames  = sensorNames;
                return true;
            }
            catch
            {
                // If we lose the mapping, try to reopen next tick
                Close();
                return false;
            }
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        public IReadOnlyList<HWiNFO_Reading> AllReadings => _readings;

        /// <summary>Find readings whose sensor name contains <paramref name="sensorNamePart"/>
        /// (case-insensitive).</summary>
        public IEnumerable<HWiNFO_Reading> BySensor(string sensorNamePart) =>
            _readings.FindAll(r => r.SensorName.Contains(sensorNamePart, StringComparison.OrdinalIgnoreCase));

        /// <summary>Find readings of a specific type whose label contains
        /// <paramref name="labelPart"/>.</summary>
        public IEnumerable<HWiNFO_Reading> ByLabel(SENSOR_READING_TYPE type, string labelPart) =>
            _readings.FindAll(r =>
                r.Type == type &&
                r.Label.Contains(labelPart, StringComparison.OrdinalIgnoreCase));

        /// <summary>Best single reading: sensor + label both contain given parts.</summary>
        public HWiNFO_Reading? Find(SENSOR_READING_TYPE type, string sensorPart, string labelPart) =>
            _readings.Find(r =>
                r.Type == type &&
                r.SensorName.Contains(sensorPart, StringComparison.OrdinalIgnoreCase) &&
                r.Label.Contains(labelPart,       StringComparison.OrdinalIgnoreCase));

        // ── Marshal helper ────────────────────────────────────────────────────

        private static T ReadStruct<T>(MemoryMappedViewAccessor acc, long offset) where T : struct
        {
            // MemoryMappedViewAccessor.Read<T> requires the offset to be
            // a multiple of the type's alignment on some runtimes. We use
            // ReadArray into a 1-element array to avoid alignment issues.
            T[] buf = new T[1];
            acc.ReadArray(offset, buf, 0, 1);
            return buf[0];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }
}
