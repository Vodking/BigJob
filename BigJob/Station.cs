using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigJob
{
    class StationContext
    {
        public SemaphoreSlim ChargerSemaphore { get; }
        public Mutex CalibrationMutex { get; }
        public CancellationTokenSource Cts { get; }
        public CountdownEvent Countdown { get; }
        public ConcurrentDictionary<int, CellResult> Results { get; } = new();
        public object ConsoleLock { get; } = new();

        public int SuccessfulCharges;
        public int SuccessfulCalibrations;
        public int InterruptedChargerWaits;
        public int InterruptedCalibrationWaits;

        public StationContext(int chargerSlots, int cellCount)
        {
            ChargerSemaphore = new SemaphoreSlim(chargerSlots, chargerSlots);
            CalibrationMutex = new Mutex();
            Cts = new CancellationTokenSource();
            Countdown = new CountdownEvent(cellCount);
        }
    }
}
