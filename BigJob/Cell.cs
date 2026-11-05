using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigJob
{
    enum CellStatus
    {
        Pending,
        Diagnostics,
        WaitingForCharger,
        Charging,
        WaitingForCalibration,
        Calibration,
        Completed,
        Canceled,
        Faulted
    }

    class Cell
    {
        public int Id { get; }
        public int DiagnosticIterations { get; }
        public int ChargeDurationMs { get; }
        public int CalibrationDurationMs { get; }

        public Cell(int id, int diagnosticIterations, int chargeDurationMs, int calibrationDurationMs)
        {
            Id = id;
            DiagnosticIterations = diagnosticIterations;
            ChargeDurationMs = chargeDurationMs;
            CalibrationDurationMs = calibrationDurationMs;
        }
    }

    class CellResult
    {
        public int Id { get; set; }
        public CellStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
