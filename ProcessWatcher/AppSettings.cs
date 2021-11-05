using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessWatcher.Settings
{
    internal record AppSettings(IList<Process> Processes, IList<Alert> Alerts);

    internal record Process(string Name, string Description);

    internal record Alert
    {
        public Alert(Process process, int? count, float? cpuThreshold, float? memoryThreshold)
        {
            Process = process;
            Count = count;
            CpuThreshold = cpuThreshold;
            MemoryThreshold = memoryThreshold;
        }

        public Process Process { get; set; } 
        public int? Count { get; set; }
        public float? CpuThreshold { get; set; } 
        public float? MemoryThreshold { get; set; }
    }
}
