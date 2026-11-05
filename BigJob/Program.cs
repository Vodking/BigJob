using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BigJob
{
    



    class Program
    {
        private static int cellCount = 20;
        private static int chargerSlots = 4;
        private static int diagnosticIterations = 100_000;
        private static int chargeMinMs = 300;
        private static int chargeMaxMs = 900;
        private static int calibrationMinMs = 100;
        private static int calibrationMaxMs = 300;
        private static int autoCancelMs = 0; // 0 = выключена

        private static StationContext context = null!;
        private static readonly Stopwatch totalTimer = new();
        private static int poolWorkerStart, poolIoStart;

        private static void ParseArguments(string[] args)
        {
            if (args.Length >= 1 && int.TryParse(args[0], out var cells)) cellCount = cells;
            if (args.Length >= 2 && int.TryParse(args[1], out var slots)) chargerSlots = slots;
            if (args.Length >= 3 && int.TryParse(args[2], out var autoCancel)) autoCancelMs = autoCancel;
        }
        private static List<Cell> CreateCells()
        {
            var rnd = new Random();
            var list = new List<Cell>();
            for (int i = 1; i <= cellCount; i++)
            {
                int chargeMs = rnd.Next(chargeMinMs, chargeMaxMs + 1);
                int calibMs = rnd.Next(calibrationMinMs, calibrationMaxMs + 1);
                list.Add(new Cell(i, diagnosticIterations, chargeMs, calibMs));
            }
            return list;
        }

        private static void Log(string message, int? cellId = null, CellStatus? status = null)
        {
            var ctx = context;
            if (ctx == null) return;

            lock (ctx.ConsoleLock)
            {
                string cellPart = cellId.HasValue ? $" [Cell {cellId}]" : "";
                string statusPart = status.HasValue ? $" | Status: {status}" : "";
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}]{cellPart} {message}{statusPart}");
            }
        }
        private static void PrintStatistics()
        {
            var ctx = context;
            int total = ctx.Results.Count;
            int completed = 0, canceled = 0, faulted = 0;
            foreach (var r in ctx.Results.Values)
            {
                switch (r.Status)
                {
                    case CellStatus.Completed: completed++; break;
                    case CellStatus.Canceled: canceled++; break;
                    case CellStatus.Faulted: faulted++; break;
                }
            }

            ThreadPool.GetAvailableThreads(out int poolWorkerEnd, out int poolIoEnd);

            Log("ИТОГОВАЯ СТАТИСТИКА");
            Log($"Всего ячеек: {total}");
            Log($"Завершено успешно: {completed}");
            Log($"Отменено: {canceled}");
            Log($"Завершено с ошибкой: {faulted}");
            Log($"Общее время выполнения: {totalTimer.ElapsedMilliseconds} мс");
            Log($"Доступно рабочих потоков пула в начале: {poolWorkerStart}, в конце: {poolWorkerEnd}");
            Log($"Успешных зарядок: {ctx.SuccessfulCharges}");
            Log($"Успешных калибровок: {ctx.SuccessfulCalibrations}");
            Log($"Прерванных ожиданий зарядки: {ctx.InterruptedChargerWaits}");
            Log($"Прерванных ожиданий калибровки: {ctx.InterruptedCalibrationWaits}");
        }
        private static void UpdateStatus(CellResult result, CellStatus status, StationContext ctx)
        {
            result.Status = status;
        }

        private static void ProcessCell(object? state)
        {
            var cell = (Cell)state!;
            var ctx = context;
            var token = ctx.Cts.Token;

            var result = new CellResult
            {
                Id = cell.Id,
                Status = CellStatus.Pending,
                StartTime = DateTime.Now
            };

            bool chargerAcquired = false;
            bool mutexAcquired = false;

            try
            {
                // 8.1 Ожидание запуска
                token.ThrowIfCancellationRequested();

                // 8.2 Диагностика
                UpdateStatus(result, CellStatus.Diagnostics, ctx);
                Console.WriteLine("Диагностика начата", cell.Id, result.Status);

                for (int i = 0; i < cell.DiagnosticIterations; i++)
                {
                    // Простые арифметические операции
                    double x = (i * 31.0 + 17.0) / 3.0;
                    if (i % 1000 == 0)
                        token.ThrowIfCancellationRequested();
                }
                Log("Диагностика завершена", cell.Id, result.Status);

                // 8.3 Зарядка
                UpdateStatus(result, CellStatus.WaitingForCharger, ctx);
                Log("Ожидание слота зарядки", cell.Id, result.Status);

                try
                {
                    chargerAcquired = ctx.ChargerSemaphore.Wait(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref ctx.InterruptedChargerWaits);
                    throw;
                }

                try
                {
                    UpdateStatus(result, CellStatus.Charging, ctx);
                    Log("Слот зарядки получен", cell.Id, result.Status);
                    Log("Зарядка начата", cell.Id, result.Status);

                    if (token.WaitHandle.WaitOne(cell.ChargeDurationMs))
                    {
                        Log("Зарядка прервана", cell.Id, result.Status);
                        UpdateStatus(result, CellStatus.Canceled, ctx);
                        token.ThrowIfCancellationRequested();
                    }

                    Log("Зарядка завершена", cell.Id, result.Status);
                    Interlocked.Increment(ref ctx.SuccessfulCharges);
                }
                finally
                {
                    if (chargerAcquired)
                    {
                        ctx.ChargerSemaphore.Release();
                        Log("Слот зарядки освобождён", cell.Id, result.Status);
                    }
                }

                // 8.4 Калибровка
                UpdateStatus(result, CellStatus.WaitingForCalibration, ctx);
                Log("Ожидание калибровочного стенда", cell.Id, result.Status);

                int waitIndex = WaitHandle.WaitAny(new[] { ctx.CalibrationMutex, token.WaitHandle });
                if (waitIndex == 1)
                {
                    // Сработал токен отмены
                    Interlocked.Increment(ref ctx.InterruptedCalibrationWaits);
                    throw new OperationCanceledException(token);
                }

                mutexAcquired = true;

                try
                {
                    // Проверка отмены после захвата мьютекса
                    if (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }

                    UpdateStatus(result, CellStatus.Calibration, ctx);
                    Log("Калибровочный стенд получен", cell.Id, result.Status);
                    Log("Калибровка начата", cell.Id, result.Status);

                    // Симуляция калибровки с поддержкой отмены
                    if (token.WaitHandle.WaitOne(cell.CalibrationDurationMs))
                    {
                        Log("Калибровка прервана", cell.Id, result.Status);
                        UpdateStatus(result, CellStatus.Canceled, ctx);
                        token.ThrowIfCancellationRequested();
                    }

                    Log("Калибровка завершена", cell.Id, result.Status);
                    Interlocked.Increment(ref ctx.SuccessfulCalibrations);
                }
                finally
                {
                    if (mutexAcquired)
                    {
                        ctx.CalibrationMutex.ReleaseMutex();
                        Log("Калибровочный стенд освобождён", cell.Id, result.Status);
                    }
                }

                // 8.5 Завершение
                UpdateStatus(result, CellStatus.Completed, ctx);
                Log("Ячейка завершена", cell.Id, result.Status);
            }
            catch (OperationCanceledException)
            {
                result.Status = CellStatus.Canceled;
                Log("Ячейка отменена", cell.Id, result.Status);
            }
            catch (Exception ex)
            {
                result.Status = CellStatus.Faulted;
                result.ErrorMessage = ex.Message;
                Log($"Произошла ошибка: {ex.Message}", cell.Id, result.Status);
            }
            finally
            {
                // Освобождение мьютекса, если он был захвачен но не освобождён в finally (на случай исключения до внутреннего finally)
                if (mutexAcquired)
                {
                    try { ctx.CalibrationMutex.ReleaseMutex(); } catch {}
                }
                if (chargerAcquired)
                {
                    try { ctx.ChargerSemaphore.Release(); } catch {}
                }

                result.EndTime = DateTime.Now;
                ctx.Results[cell.Id] = result;
                ctx.Countdown.Signal();
            }
        }
        static void Main(string[] args)
        {
            ParseArguments(args);

            ParseArguments(args);

            Console.WriteLine("Станция испытания аккумуляторных ячеек ");
            ThreadPool.GetAvailableThreads(out poolWorkerStart, out poolIoStart);
            Log($"Доступно рабочих потоков пула в начале: {poolWorkerStart}, I/O: {poolIoStart}");

            context = new StationContext(chargerSlots, cellCount);
            var cells = CreateCells();

            // Настройка отмены по Ctrl+C
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Log("Запрошена аварийная остановка (Ctrl+C)");
                context.Cts.Cancel();
            };

            // Автоматическая отмена по таймауту
            if (autoCancelMs > 0)
            {
                Log($"Включена автоматическая отмена через {autoCancelMs} мс");
                context.Cts.CancelAfter(autoCancelMs);
            }

            Log("Партия создана. Запуск обработки...");
            totalTimer.Start();

            // Постановка каждой ячейки в пул потоков
            foreach (var cell in cells)
            {
                Log($"Ячейка {cell.Id} поставлена в очередь", cell.Id, CellStatus.Pending);
                ThreadPool.QueueUserWorkItem(ProcessCell, cell);
            }

            // Ожидание завершения всех рабочих элементов с лимитом 10 секунд
            bool completed = context.Countdown.Wait(TimeSpan.FromSeconds(10));
            if (!completed)
            {
                Log("ОШИБКА: превышен лимит ожидания завершения рабочих элементов!");
            }

            totalTimer.Stop();

            // Итоговая статистика
            PrintStatistics();

            // Освобождение ресурсов
            context.ChargerSemaphore.Dispose();
            context.CalibrationMutex.Dispose();
            context.Cts.Dispose();
            context.Countdown.Dispose();

            Log("Обработка партии завершена");


        }
    }
}
