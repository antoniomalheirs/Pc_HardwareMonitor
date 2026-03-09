using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace Monitor_Pc.Utilities
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  HWiNFO64 Shared Memory Reader
    //
    //  ⚠️  REQUISITO: No HWiNFO64:
    //       Settings → General → ✔ Shared Memory Support → OK
    //       Depois feche e reabra a janela de sensores do HWiNFO.
    //
    //  Signature correta verificada contra o gist oficial de namazso:
    //    ((uint32_t)'SiWH') = 0x53695748
    //    Bytes em memória: H=0x48, W=0x57, i=0x69, S=0x53
    //    Lido como uint32 little-endian = 0x53695748
    // ═══════════════════════════════════════════════════════════════════════════

    public enum SENSOR_READING_TYPE : uint
    {
        SENSOR_TYPE_NONE    = 0,
        SENSOR_TYPE_TEMP    = 1,
        SENSOR_TYPE_VOLT    = 2,
        SENSOR_TYPE_FAN     = 3,
        SENSOR_TYPE_POWER   = 4,
        SENSOR_TYPE_CLOCK   = 5,
        SENSOR_TYPE_USAGE   = 6,
        SENSOR_TYPE_OTHER   = 7,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HWiNFO_SENSORS_SHARED_MEM2
    {
        public uint  dwSignature;
        public uint  dwVersion;
        public uint  dwRevision;
        public long  poll_time;
        public uint  dwOffsetOfSensorSection;
        public uint  dwSizeOfSensorElement;
        public uint  dwNumSensorElements;
        public uint  dwOffsetOfReadingSection;
        public uint  dwSizeOfReadingElement;
        public uint  dwNumReadingElements;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct HWiNFO_SENSORS_SENSOR_ELEMENT
    {
        public uint dwSensorID;
        public uint dwSensorInst;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szSensorNameOrig;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szSensorNameUser;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct HWiNFO_SENSORS_READING_ELEMENT
    {
        public SENSOR_READING_TYPE tReading;
        public uint   dwSensorIndex;
        public uint   dwReadingID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szLabelOrig;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szLabelUser;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]  public string szUnit;
        public double Value;
        public double ValueMin;
        public double ValueMax;
        public double ValueAvg;
    }

    public class HWiNFO_Reading
    {
        public SENSOR_READING_TYPE Type        { get; init; }
        public uint                SensorIndex { get; init; }
        public uint                ReadingId   { get; init; }
        public string              SensorName  { get; init; } = "";
        public string              Label       { get; init; } = "";
        public string              Unit        { get; init; } = "";
        public double              Value       { get; set;  }
        public double              Min         { get; set;  }
        public double              Max         { get; set;  }
        public double              Avg         { get; set;  }
        public override string ToString() => $"[{Type}] {SensorName} / {Label} = {Value:F3} {Unit}";
    }

    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class HWiNFOReader : IDisposable
    {
        private static readonly string[] SHM_NAMES =
        {
            "Global\\HWiNFO_SENS_SM2",
            "HWiNFO_SENS_SM2",
            "Local\\HWiNFO_SENS_SM2"
        };

        private static readonly string[] MUTEX_NAMES =
        {
            "Global\\HWiNFO_SM2_MUTEX",
            "HWiNFO_SM2_MUTEX",
            "Local\\HWiNFO_SM2_MUTEX"
        };

        // ✅ VALOR CORRETO: bytes "HWiS" em memória lidos como uint32 little-endian
        //    = 0x53 << 24 | 0x69 << 16 | 0x57 << 8 | 0x48 = 0x53695748
        //    Equivalente ao 'SiWH' do gist namazso (MSVC big-endian char literal)
        private const uint SIGNATURE = 0x53695748;

        private MemoryMappedFile?         _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private bool                      _open;
        private bool                      _disposed;

        private List<HWiNFO_Reading> _readings      = new();
        private List<string>         _sensorNames   = new();
        private string               _activeShmName = SHM_NAMES[0];
        private string?              _activeMutexName;

        public bool   IsAvailable       { get; private set; }
        public string DiagnosticMessage { get; private set; } = "";

        // ── Open ─────────────────────────────────────────────────────────────

        public bool TryOpen()
        {
            if (_open) return true;

            Exception? lastError = null;
            foreach (var shmName in SHM_NAMES)
            {
                try
                {
                    _mmf      = MemoryMappedFile.OpenExisting(shmName, MemoryMappedFileRights.Read);
                    _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    _activeShmName = shmName;
                    _open     = true;
                    DiagnosticMessage = "";
                    return true;
                }
                catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    Close();
                }
            }

            if (lastError is UnauthorizedAccessException)
            {
                DiagnosticMessage =
                    "Acesso negado ao Shared Memory do HWiNFO.\n" +
                    "Tente executar HWiNFO e este Monitor com o mesmo nível de privilégio\n" +
                    "(ambos normal ou ambos Administrador).";
            }
            else if (lastError is FileNotFoundException || lastError == null)
            {
                DiagnosticMessage =
                    "Memória compartilhada do HWiNFO não encontrada.\n" +
                    "Nomes tentados: Global\\HWiNFO_SENS_SM2, HWiNFO_SENS_SM2, Local\\HWiNFO_SENS_SM2.\n" +
                    "No HWiNFO64: Settings → General → ✔ Shared Memory Support.\n" +
                    "Depois feche e reabra a janela de sensores do HWiNFO.";
            }
            else
            {
                DiagnosticMessage = $"Erro ao abrir SHM: {lastError.GetType().Name}\n{lastError.Message}";
            }

            _open = false;
            return false;
        }

        public void Close()
        {
            _accessor?.Dispose(); _accessor = null;
            _mmf?.Dispose();      _mmf      = null;
            _open = false;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        public bool Refresh()
        {
            if (!_open && !TryOpen()) return false;

            Mutex? hwMutex    = null;
            bool   mutexOwned = false;
            _activeMutexName  = null;

            foreach (var mutexName in MUTEX_NAMES)
            {
                try
                {
                    hwMutex          = Mutex.OpenExisting(mutexName);
                    mutexOwned       = hwMutex.WaitOne(300);
                    _activeMutexName = mutexName;
                    break;
                }
                catch
                {
                    /* mutex opcional — versões antigas do HWiNFO não exportam */
                }
            }

            try   { return ReadSharedMemory(); }
            finally
            {
                if (mutexOwned) hwMutex?.ReleaseMutex();
                hwMutex?.Dispose();
            }
        }

        private bool ReadSharedMemory()
        {
            try
            {
                _accessor!.Read(0, out HWiNFO_SENSORS_SHARED_MEM2 hdr);

                if (hdr.dwSignature != SIGNATURE)
                {
                    // Mostra o valor real para diagnóstico
                    DiagnosticMessage =
                        $"Assinatura inválida: 0x{hdr.dwSignature:X8}\n" +
                        $"Esperado: 0x{SIGNATURE:X8}\n" +
                        $"Segmento SHM ativo: {_activeShmName}\n" +
                        "HWiNFO pode estar reiniciando.\n" +
                        "Certifique-se de que o Shared Memory\n" +
                        "está ativado nas configurações.";
                    _open = false;
                    return false;
                }

                // ── Sensor names ──────────────────────────────────────────────
                var sensorNames = new List<string>((int)hdr.dwNumSensorElements);
                for (uint i = 0; i < hdr.dwNumSensorElements; i++)
                {
                    long offset = hdr.dwOffsetOfSensorSection + i * hdr.dwSizeOfSensorElement;
                    var  elem   = ReadStruct<HWiNFO_SENSORS_SENSOR_ELEMENT>(_accessor, offset);
                    string name = !string.IsNullOrWhiteSpace(elem.szSensorNameUser)
                        ? elem.szSensorNameUser
                        : elem.szSensorNameOrig;
                    sensorNames.Add(name ?? $"Sensor_{i}");
                }

                // ── Readings ──────────────────────────────────────────────────
                var readings = new List<HWiNFO_Reading>((int)hdr.dwNumReadingElements);
                for (uint i = 0; i < hdr.dwNumReadingElements; i++)
                {
                    long offset = hdr.dwOffsetOfReadingSection + i * hdr.dwSizeOfReadingElement;
                    var  elem   = ReadStruct<HWiNFO_SENSORS_READING_ELEMENT>(_accessor, offset);

                    if (elem.tReading == SENSOR_READING_TYPE.SENSOR_TYPE_NONE) continue;

                    string sensorName = elem.dwSensorIndex < sensorNames.Count
                        ? sensorNames[(int)elem.dwSensorIndex]
                        : $"Sensor_{elem.dwSensorIndex}";

                    string label = !string.IsNullOrWhiteSpace(elem.szLabelUser)
                        ? elem.szLabelUser
                        : elem.szLabelOrig;

                    readings.Add(new HWiNFO_Reading
                    {
                        Type        = elem.tReading,
                        SensorIndex = elem.dwSensorIndex,
                        ReadingId   = elem.dwReadingID,
                        SensorName  = sensorName,
                        Label       = label ?? "",
                        Unit        = elem.szUnit ?? "",
                        Value       = elem.Value,
                        Min         = elem.ValueMin,
                        Max         = elem.ValueMax,
                        Avg         = elem.ValueAvg,
                    });
                }

                _readings         = readings;
                _sensorNames      = sensorNames;
                IsAvailable       = true;
                DiagnosticMessage = "";
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticMessage = $"Erro ao ler SHM:\n{ex.GetType().Name}\n{ex.Message}";
                Close();
                return false;
            }
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        public IReadOnlyList<HWiNFO_Reading> AllReadings => _readings;

        public IEnumerable<HWiNFO_Reading> BySensor(string part) =>
            _readings.FindAll(r => r.SensorName.Contains(part, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<HWiNFO_Reading> ByLabel(SENSOR_READING_TYPE type, string part) =>
            _readings.FindAll(r =>
                r.Type == type &&
                r.Label.Contains(part, StringComparison.OrdinalIgnoreCase));

        public HWiNFO_Reading? Find(SENSOR_READING_TYPE type, string sensorPart, string labelPart) =>
            _readings.Find(r =>
                r.Type == type &&
                r.SensorName.Contains(sensorPart, StringComparison.OrdinalIgnoreCase) &&
                r.Label.Contains(labelPart,       StringComparison.OrdinalIgnoreCase));

        // ── Marshal helper ────────────────────────────────────────────────────

        private static T ReadStruct<T>(MemoryMappedViewAccessor acc, long offset) where T : struct
        {
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
