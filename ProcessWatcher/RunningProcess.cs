using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessWatcher
{
    internal record RunningProcess
    {
        public RunningProcess(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public int Count { get; set; }
        public float CpuUsage { get; set; }
        public float MemoryUsage { get; set; }
    }
}
