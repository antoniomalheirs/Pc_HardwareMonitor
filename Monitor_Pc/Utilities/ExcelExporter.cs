using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace Monitor_Pc.Utilities
{
    /// <summary>
    /// Lê um CSV gerado pelo TelemetryLogger e exporta um .xlsx profissional
    /// com abas separadas por categoria, formatação condicional e cores.
    /// </summary>
    public static class ExcelExporter
    {
        // ── Category definitions ──────────────────────────────────────────────

        private static readonly (string SheetName, string Prefix, string SensorType, XLColor HeaderColor, XLColor AccentColor, string NumberFormat)[] Categories =
        {
            ("CPU Frequências",  "CPU",  "Clock",       XLColor.FromHtml("#7B1FA2"), XLColor.FromHtml("#CE93D8"), "#,##0.0 \"MHz\""),
            ("CPU Tensões",      "CPU",  "Voltage",     XLColor.FromHtml("#E65100"), XLColor.FromHtml("#FFAB91"), "0.000 \"V\""),
            ("CPU Temperaturas", "CPU",  "Temperature", XLColor.FromHtml("#C62828"), XLColor.FromHtml("#EF9A9A"), "0.0 \"°C\""),
            ("CPU Potência",     "CPU",  "Power",       XLColor.FromHtml("#00695C"), XLColor.FromHtml("#80CBC4"), "0.0 \"W\""),
            ("GPU Frequências",  "GPU",  "Clock",       XLColor.FromHtml("#1565C0"), XLColor.FromHtml("#90CAF9"), "#,##0.0 \"MHz\""),
            ("GPU Tensões",      "GPU",  "Voltage",     XLColor.FromHtml("#F57F17"), XLColor.FromHtml("#FFF176"), "0.000 \"V\""),
            ("GPU Temperaturas", "GPU",  "Temperature", XLColor.FromHtml("#B71C1C"), XLColor.FromHtml("#EF9A9A"), "0.0 \"°C\""),
            ("GPU Potência",     "GPU",  "Power",       XLColor.FromHtml("#004D40"), XLColor.FromHtml("#80CBC4"), "0.0 \"W\""),
            ("MB Tensões",       "MB",   "Voltage",     XLColor.FromHtml("#FF6F00"), XLColor.FromHtml("#FFE082"), "0.000 \"V\""),
            ("MB Temperaturas",  "MB",   "Temperature", XLColor.FromHtml("#D84315"), XLColor.FromHtml("#FFAB91"), "0.0 \"°C\""),
        };

        // ── Export ─────────────────────────────────────────────────────────────

        public static string ExportCsvToExcel(string csvPath)
        {
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("CSV não encontrado", csvPath);

            var (headers, rows) = ParseCsv(csvPath);

            string xlsxPath = Path.ChangeExtension(csvPath, ".xlsx");

            using var wb = new XLWorkbook();

            // ── Resumo (overview) ─────────────────────────────────────────
            CreateOverviewSheet(wb, headers, rows, csvPath);

            // ── Abas por categoria ────────────────────────────────────────
            foreach (var cat in Categories)
            {
                var colIndices = FindColumns(headers, cat.Prefix, cat.SensorType);
                if (colIndices.Count == 0) continue;

                CreateCategorySheet(wb, cat.SheetName, headers, rows,
                    colIndices, cat.HeaderColor, cat.AccentColor, cat.NumberFormat, cat.SensorType);
            }

            // ── Aba "Dados Brutos" ────────────────────────────────────────
            CreateRawSheet(wb, headers, rows);

            wb.SaveAs(xlsxPath);
            return xlsxPath;
        }

        // ── Overview sheet ────────────────────────────────────────────────────

        private static void CreateOverviewSheet(IXLWorkbook wb,
            string[] headers, List<string[]> rows, string csvPath)
        {
            var ws = wb.AddWorksheet("Resumo");
            ws.SheetView.View = XLSheetViewOptions.Normal;

            // Title
            ws.Cell(1, 1).Value = "MONITOR PC — RELATÓRIO DE TELEMETRIA";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 18;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Range(1, 1, 1, 5).Merge();
            ws.Range(1, 1, 1, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#1A1A2E");
            ws.Range(1, 1, 1, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 40;

            // Stats
            int r = 3;
            void AddStat(string label, string value)
            {
                ws.Cell(r, 1).Value = label;
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#666666");
                ws.Cell(r, 2).Value = value;
                ws.Cell(r, 2).Style.Font.Bold = true;
                r++;
            }

            AddStat("Arquivo CSV:", Path.GetFileName(csvPath));
            AddStat("Total de Leituras:", rows.Count.ToString("N0"));

            if (rows.Count > 0)
            {
                AddStat("Primeira Leitura:", rows[0][0]);
                AddStat("Última Leitura:", rows[^1][0]);
            }

            AddStat("Sensores Monitorados:", (headers.Length - 1).ToString());

            r += 1;
            ws.Cell(r, 1).Value = "ABAS DISPONÍVEIS";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 13;
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#1A1A2E");
            r++;

            foreach (var cat in Categories)
            {
                var colCount = FindColumns(headers, cat.Prefix, cat.SensorType).Count;
                if (colCount == 0) continue;

                ws.Cell(r, 1).Value = $"📊 {cat.SheetName}";
                ws.Cell(r, 1).Style.Font.FontColor = cat.HeaderColor;
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Cell(r, 2).Value = $"{colCount} sensores";
                ws.Cell(r, 2).Style.Font.FontColor = XLColor.FromHtml("#999999");
                r++;
            }

            r += 1;
            ws.Cell(r, 1).Value = "💡 DICA: Selecione colunas de dados em qualquer aba";
            ws.Cell(r + 1, 1).Value = "    e use Inserir → Gráfico para criar visualizações!";
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#0288D1");
            ws.Cell(r + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#0288D1");

            ws.Column(1).Width = 30;
            ws.Column(2).Width = 40;
        }

        // ── Category sheet ────────────────────────────────────────────────────

        private static void CreateCategorySheet(IXLWorkbook wb,
            string sheetName, string[] headers, List<string[]> rows,
            List<int> colIndices,
            XLColor headerColor, XLColor accentColor, string numberFormat, string sensorType)
        {
            var ws = wb.AddWorksheet(sheetName);

            // ── Header row ────────────────────────────────────────────────
            ws.Cell(1, 1).Value = "Timestamp";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#333333");

            for (int c = 0; c < colIndices.Count; c++)
            {
                string colName = headers[colIndices[c]];
                // Remove prefix (CPU_Clock_, GPU_Voltage_, etc.)
                string cleanName = CleanColumnName(colName);

                ws.Cell(1, c + 2).Value = cleanName;
                ws.Cell(1, c + 2).Style.Font.Bold = true;
                ws.Cell(1, c + 2).Style.Font.FontColor = XLColor.White;
                ws.Cell(1, c + 2).Style.Fill.BackgroundColor = headerColor;
                ws.Cell(1, c + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Stats row (Min / Max / Avg) ───────────────────────────────
            int statsRow = 2;
            ws.Cell(statsRow, 1).Value = "📈 MIN / MÁX / MÉD";
            ws.Cell(statsRow, 1).Style.Font.Bold = true;
            ws.Cell(statsRow, 1).Style.Font.FontColor = headerColor;
            ws.Cell(statsRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");

            for (int c = 0; c < colIndices.Count; c++)
            {
                var values = GetNumericValues(rows, colIndices[c]);
                if (values.Count > 0)
                {
                    ws.Cell(statsRow, c + 2).Value =
                        $"{values.Min():F2} / {values.Max():F2} / {values.Average():F2}";
                }
                else
                {
                    ws.Cell(statsRow, c + 2).Value = "--";
                }
                ws.Cell(statsRow, c + 2).Style.Font.FontSize = 9;
                ws.Cell(statsRow, c + 2).Style.Font.FontColor = XLColor.FromHtml("#555555");
                ws.Cell(statsRow, c + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                ws.Cell(statsRow, c + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Data rows ─────────────────────────────────────────────────
            int dataStart = 3;
            for (int i = 0; i < rows.Count; i++)
            {
                int row = dataStart + i;
                ws.Cell(row, 1).Value = rows[i][0]; // Timestamp

                for (int c = 0; c < colIndices.Count; c++)
                {
                    int colIdx = colIndices[c];
                    if (colIdx < rows[i].Length &&
                        double.TryParse(rows[i][colIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        ws.Cell(row, c + 2).Value = val;
                        ws.Cell(row, c + 2).Style.NumberFormat.Format = numberFormat;
                    }
                }

                // Zebra striping
                if (i % 2 == 1)
                {
                    ws.Range(row, 1, row, colIndices.Count + 1)
                        .Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFAFA");
                }
            }

            // ── Conditional formatting for data ───────────────────────────
            if (rows.Count > 0)
            {
                ApplyConditionalFormatting(ws, dataStart, rows.Count, colIndices, headers, sensorType);
            }

            // ── Formatting ────────────────────────────────────────────────
            ws.Row(1).Height = 28;
            ws.SheetView.FreezeRows(2);
            ws.Column(1).Width = 24;
            for (int c = 0; c < colIndices.Count; c++)
                ws.Column(c + 2).Width = 18;

            ws.RangeUsed()?.SetAutoFilter();
        }

        // ── Raw data sheet ────────────────────────────────────────────────────

        private static void CreateRawSheet(IXLWorkbook wb,
            string[] headers, List<string[]> rows)
        {
            var ws = wb.AddWorksheet("Dados Brutos");

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
                ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#263238");
                ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                for (int c = 0; c < rows[i].Length; c++)
                {
                    if (c == 0)
                    {
                        ws.Cell(i + 2, c + 1).Value = rows[i][c];
                    }
                    else if (double.TryParse(rows[i][c], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        ws.Cell(i + 2, c + 1).Value = val;
                    }
                    else
                    {
                        ws.Cell(i + 2, c + 1).Value = rows[i][c];
                    }
                }
            }

            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void ApplyConditionalFormatting(IXLWorksheet ws, int dataStart, int rowCount, List<int> colIndices, string[] headers, string sensorType)
        {
            for (int c = 0; c < colIndices.Count; c++)
            {
                string colName = headers[colIndices[c]];
                var range = ws.Range(dataStart, c + 2, dataStart + rowCount - 1, c + 2);

                if (sensorType == "Temperature")
                {
                    // For temps: < 40 Green, ~75 Yellow, > 90 Red
                    range.AddConditionalFormat().ColorScale()
                        .Minimum(XLCFContentType.Number, "40", XLColor.FromHtml("#C8E6C9"))  // Green
                        .Midpoint(XLCFContentType.Number, "75", XLColor.FromHtml("#FFF9C4")) // Yellow
                        .Maximum(XLCFContentType.Number, "90", XLColor.FromHtml("#FFCDD2")); // Red
                }
                else if (sensorType == "Clock")
                {
                    // For Clocks (MHz): High is good (green), low is idle (yellowish)
                    range.AddConditionalFormat().ColorScale()
                        .Minimum(XLCFContentType.Percentile, "10", XLColor.FromHtml("#FFF9C4")) // Yellowish (idle)
                        .Midpoint(XLCFContentType.Percentile, "50", XLColor.FromHtml("#E8F5E9")) // Light Green
                        .Maximum(XLCFContentType.Percentile, "90", XLColor.FromHtml("#A5D6A7")); // Green (turbo)
                }
                else if (sensorType == "Power")
                {
                    // Power: Informative. Lower is green, higher is orange.
                    range.AddConditionalFormat().ColorScale()
                        .Minimum(XLCFContentType.Percentile, "10", XLColor.FromHtml("#C8E6C9"))  // Green
                        .Midpoint(XLCFContentType.Percentile, "50", XLColor.FromHtml("#FFF9C4")) // Yellow
                        .Maximum(XLCFContentType.Percentile, "90", XLColor.FromHtml("#FFCC80")); // Orange
                }
                else if (sensorType == "Voltage")
                {
                    if (colName.Contains("+12V"))
                    {
                        // +12V tolerance is generally 11.4V to 12.6V
                        range.AddConditionalFormat().ColorScale()
                            .Minimum(XLCFContentType.Number, "11.4", XLColor.FromHtml("#FFCDD2"))  // Red (too low)
                            .Midpoint(XLCFContentType.Number, "12.0", XLColor.FromHtml("#C8E6C9")) // Green (ideal)
                            .Maximum(XLCFContentType.Number, "12.6", XLColor.FromHtml("#FFCDD2")); // Red (too high)
                    }
                    else if (colName.Contains("+5V"))
                    {
                        // +5V tolerance is generally 4.75V to 5.25V
                        range.AddConditionalFormat().ColorScale()
                            .Minimum(XLCFContentType.Number, "4.75", XLColor.FromHtml("#FFCDD2"))
                            .Midpoint(XLCFContentType.Number, "5.0", XLColor.FromHtml("#C8E6C9"))
                            .Maximum(XLCFContentType.Number, "5.25", XLColor.FromHtml("#FFCDD2"));
                    }
                    else if (colName.Contains("3VCC") || colName.Contains("3VSB") || colName.Contains("VBAT") || colName.Contains("AVSB"))
                    {
                        // +3.3V tolerance is generally 3.13V to 3.47V
                        range.AddConditionalFormat().ColorScale()
                            .Minimum(XLCFContentType.Number, "3.13", XLColor.FromHtml("#FFCDD2"))
                            .Midpoint(XLCFContentType.Number, "3.3", XLColor.FromHtml("#C8E6C9"))
                            .Maximum(XLCFContentType.Number, "3.47", XLColor.FromHtml("#FFCDD2"));
                    }
                    else
                    {
                        // VCore or other CPU/GPU voltages.
                        // Gradient from Green (low/idle) to Yellow (mid) to Orange (high).
                        range.AddConditionalFormat().ColorScale()
                            .Minimum(XLCFContentType.Percentile, "10", XLColor.FromHtml("#C8E6C9"))
                            .Midpoint(XLCFContentType.Percentile, "50", XLColor.FromHtml("#FFF9C4"))
                            .Maximum(XLCFContentType.Percentile, "90", XLColor.FromHtml("#FFCC80"));
                    }
                }
                else
                {
                    // Fallback
                    range.AddConditionalFormat().ColorScale()
                        .Minimum(XLCFContentType.Percentile, "10", XLColor.FromHtml("#C8E6C9"))
                        .Midpoint(XLCFContentType.Percentile, "50", XLColor.FromHtml("#FFF9C4"))
                        .Maximum(XLCFContentType.Percentile, "90", XLColor.FromHtml("#FFCDD2"));
                }
            }
        }

        private static (string[] Headers, List<string[]> Rows) ParseCsv(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return (Array.Empty<string>(), new List<string[]>());

            var headers = lines[0].Split(',');
            var rows = new List<string[]>(lines.Length - 1);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                rows.Add(lines[i].Split(','));
            }

            return (headers, rows);
        }

        private static List<int> FindColumns(string[] headers, string prefix, string sensorType)
        {
            var result = new List<int>();
            string pattern = $"{prefix}_{sensorType}_";

            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    result.Add(i);
            }
            return result;
        }

        private static string CleanColumnName(string colName)
        {
            // "CPU_Clock_Core_#0" → "Core #0"
            // "GPU_Voltage_GPU_VDDC" → "GPU VDDC"
            var parts = colName.Split('_');
            if (parts.Length > 2)
            {
                return string.Join(" ", parts.Skip(2)).Replace("_", " ");
            }
            return colName;
        }

        private static List<double> GetNumericValues(List<string[]> rows, int colIdx)
        {
            var values = new List<double>();
            foreach (var row in rows)
            {
                if (colIdx < row.Length &&
                    double.TryParse(row[colIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                {
                    values.Add(val);
                }
            }
            return values;
        }
    }
}
