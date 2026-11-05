using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BigJob
{
    class Program
    {

        static void Main(string[] args)
        {
            int cellCountInput;
            int chargingSlotsInput;
            int autoCancelMsInput;
            Console.WriteLine("Станция испытания аккумуляторных ячеек");
            Console.Write("Введите количество ячеек: ");
            cellCountInput = Convert.ToInt32(Console.ReadLine());
            Console.Write("Введите количество зарядников: ");
            chargingSlotsInput = Convert.ToInt32(Console.ReadLine());
            Console.Write("Введите количество миллисекунд до таймаута(0 - выключена): ");
            autoCancelMsInput = Convert.ToInt32(Console.ReadLine());

            Processor.Start(cellCountInput, chargingSlotsInput, autoCancelMsInput);
        }
    }
}
