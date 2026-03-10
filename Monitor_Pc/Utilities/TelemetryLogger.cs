using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Monitor_Pc.Models;

namespace Monitor_Pc.Utilities
{
    /// <summary>
    /// Grava um CSV contínuo com frequências, tensões e temperaturas de CPU/GPU.
    /// Projetado para diagnosticar quedas de tensão (blackout de tela / PSU instável).
    ///
    /// - Cabeçalho dinâmico gerado a partir dos sensores disponíveis
    /// - Flush automático a cada N linhas ou em anomalia de tensão/frequência
    /// - Arquivo rotativo com timestamp no nome
    /// </summary>
    public sealed class TelemetryLogger : IDisposable
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const int    FLUSH_INTERVAL      = 5;           // linhas entre flushes
        private const double VOLTAGE_LOW_THRESH   = 0.50;       // V — abaixo disso = anomalia
        private const double FREQ_DROP_PERCENT    = 40.0;       // % de queda para flush imediato

        private static readonly string[] RELEVANT_HW_TYPES =
            { "Cpu", "GpuNvidia", "GpuAmd", "GpuIntel", "Motherboard" };

        private static readonly string[] RELEVANT_SENSOR_TYPES =
            { "Clock", "Voltage", "Temperature", "Power" };

        // ── State ─────────────────────────────────────────────────────────────
        private StreamWriter? _writer;
        private string        _filePath = "";
        private List<string>  _columns  = new();
        private int           _linesSinceFlush;
        private long          _entryCount;
        private bool          _headerWritten;
        private bool          _disposed;

        // Para detecção de anomalias
        private readonly Dictionary<string, double> _prevValues = new();

        // ── Public properties ─────────────────────────────────────────────────
        public bool   IsActive   => _writer != null;
        public string FilePath   => _filePath;
        public long   EntryCount => _entryCount;

        // ── Start / Stop ──────────────────────────────────────────────────────

        public void Start()
        {
            if (_writer != null) return;

            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MonitorPC_Logs");
            Directory.CreateDirectory(logsDir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _filePath = Path.Combine(logsDir, $"MonitorLog_{timestamp}.csv");

            _writer          = new StreamWriter(_filePath, false, Encoding.UTF8);
            _headerWritten   = false;
            _linesSinceFlush = 0;
            _entryCount      = 0;
            _columns.Clear();
            _prevValues.Clear();
        }

        public void Stop()
        {
            if (_writer == null) return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch { /* best-effort */ }
            finally
            {
                _writer = null;
            }
        }

        // ── Log entry ─────────────────────────────────────────────────────────

        public void LogEntry(IEnumerable<HardwareItem> items)
        {
            if (_writer == null) return;

            try
            {
                var sensors = CollectRelevantSensors(items);

                if (!_headerWritten)
                {
                    WriteHeader(sensors);
                    _headerWritten = true;
                }

                WriteRow(sensors);
            }
            catch
            {
                // Logger nunca deve derrubar a aplicação
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private List<SensorSnapshot> CollectRelevantSensors(IEnumerable<HardwareItem> items)
        {
            var result = new List<SensorSnapshot>();

            foreach (var hw in items)
            {
                if (!RELEVANT_HW_TYPES.Contains(hw.HardwareType)) continue;

                string prefix = hw.HardwareType switch
                {
                    "Cpu"         => "CPU",
                    "Motherboard" => "MB",
                    _             => "GPU"
                };

                foreach (var category in hw.Categories)
                {
                    foreach (var sensor in category.Sensors)
                    {
                        if (!RELEVANT_SENSOR_TYPES.Contains(sensor.SensorType)) continue;
                        if (!sensor.IsValid) continue;

                        string colName = $"{prefix}_{sensor.SensorType}_{CleanName(sensor.Name)}";
                        result.Add(new SensorSnapshot(colName, sensor.SensorType, sensor.Value ?? 0f));
                    }
                }
            }

            return result;
        }

        private void WriteHeader(List<SensorSnapshot> sensors)
        {
            _columns = sensors.Select(s => s.ColumnName).ToList();

            var sb = new StringBuilder();
            sb.Append("Timestamp");
            foreach (var col in _columns)
            {
                sb.Append(',');
                sb.Append(col);
            }
            _writer!.WriteLine(sb.ToString());
            _writer.Flush();
        }

        private void WriteRow(List<SensorSnapshot> sensors)
        {
            var lookup = new Dictionary<string, double>();
            foreach (var s in sensors)
            {
                lookup[s.ColumnName] = s.Value;
            }

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));

            bool anomaly = false;

            foreach (var col in _columns)
            {
                sb.Append(',');
                if (lookup.TryGetValue(col, out double val))
                {
                    sb.Append(val.ToString("F3", CultureInfo.InvariantCulture));

                    // Detecção de anomalia
                    if (IsAnomaly(col, val))
                        anomaly = true;

                    _prevValues[col] = val;
                }
                else
                {
                    sb.Append("");  // sensor desapareceu — campo vazio
                }
            }

            // Detectar novas colunas que não estavam no header original
            foreach (var s in sensors)
            {
                if (!_columns.Contains(s.ColumnName))
                {
                    // Adiciona ao final — CSV ficará com mais colunas, mas sem perder dados
                    _columns.Add(s.ColumnName);
                    sb.Append(',');
                    sb.Append(s.Value.ToString("F3", CultureInfo.InvariantCulture));
                }
            }

            _writer!.WriteLine(sb.ToString());
            _entryCount++;
            _linesSinceFlush++;

            // Flush normal ou de emergência
            if (anomaly || _linesSinceFlush >= FLUSH_INTERVAL)
            {
                _writer.Flush();
                _linesSinceFlush = 0;
            }
        }

        private bool IsAnomaly(string colName, double currentValue)
        {
            // Tensão perigosamente baixa
            if (colName.Contains("Voltage") && currentValue > 0 && currentValue < VOLTAGE_LOW_THRESH)
                return true;

            // Queda brusca de frequência
            if (colName.Contains("Clock") && _prevValues.TryGetValue(colName, out double prev) && prev > 100)
            {
                double dropPercent = (prev - currentValue) / prev * 100.0;
                if (dropPercent >= FREQ_DROP_PERCENT)
                    return true;
            }

            return false;
        }

        private static string CleanName(string name)
        {
            // Remove caracteres que atrapalham CSV e cria um nome de coluna limpo
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == ',' || c == '"' || c == '\n' || c == '\r')
                    continue;
                if (c == ' ' || c == '#' || c == '/' || c == '\\')
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        // ── Inner types ───────────────────────────────────────────────────────

        private record SensorSnapshot(string ColumnName, string SensorType, double Value);
    }
}
