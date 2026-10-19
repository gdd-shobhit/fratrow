using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FratRow.Social
{
    /// <summary>Windows job closes the helper even if the game crashes or is forcibly terminated.</summary>
    internal sealed class ChildProcessLifetime : IDisposable
    {
        SafeFileHandle job;
        public ChildProcessLifetime(Process process)
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new ExtendedLimits(); info.basic.limitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf<ExtendedLimits>(); IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, memory, false);
                if (!SetInformationJobObject(job, 9, memory, (uint)size) || !AssignProcessToJobObject(job, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch { job.Dispose(); throw; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public void Dispose() { job?.Dispose(); job = null; }
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long perProcessUserTime, perJobUserTime;
            public uint limitFlags;
            public UIntPtr minimumWorkingSet, maximumWorkingSet;
            public uint activeProcessLimit;
            public UIntPtr affinity;
            public uint priorityClass, schedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong readOperations, writeOperations, otherOperations, readBytes, writeBytes, otherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
        {
            public BasicLimits basic;
            public IoCounters io;
            public UIntPtr processMemoryLimit, jobMemoryLimit, peakProcessMemoryUsed, peakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    }
}
