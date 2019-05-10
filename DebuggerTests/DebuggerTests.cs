using System;
using System.IO;
using System.Net;
using System.Threading;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using Mono.Debugging.Tests;
using MonoDevelop.Projects.Text;
using NUnit.Framework;
using Diag = System.Diagnostics;

namespace DebuggerTests
{
    [TestFixture]
    public abstract class DebuggerTests
    {
        public VirtualMachine vm { get; set; }
        public MethodMirror entry_point { get; set; }
        protected bool forceExit;
        protected StepEventRequest step_req;

        ITextFile SourceFile;
        protected readonly ManualResetEvent targetStoppedEvent = new ManualResetEvent(false);
        SourceLocation lastStoppedPosition;

        // No other way to pass arguments to the tests ?
        public static bool listening = Environment.GetEnvironmentVariable("DBG_SUSPEND") != null;
        public static string runtime = Environment.GetEnvironmentVariable("DBG_RUNTIME");
        public static string agent_args = Environment.GetEnvironmentVariable("DBG_AGENT_ARGS");

        protected void Start(params string[] args)
        {
            Start(false, args);
        }

        protected void Start(bool forceExit, params string[] args)
        {
            this.forceExit = forceExit;

            if (!listening)
            {
                var pi = CreateStartInfo(args);
                vm = VirtualMachineManager.Launch(pi, new LaunchOptions { AgentArgs = agent_args });
            }
            else
            {
                var ep = new IPEndPoint(IPAddress.Any, 10000);
                Console.WriteLine("Listening on " + ep + "...");
                vm = VirtualMachineManager.Listen(ep);
            }

            var load_req = vm.CreateAssemblyLoadRequest();
            load_req.Enable();

            Event vmstart = GetNextEvent();
            Assert.AreEqual(EventType.VMStart, vmstart.EventType);

            SourceFile = ReadFile(SourceFilePath());

            vm.Resume();

            entry_point = null;
            step_req = null;

            Event e;

            /* Find out the entry point */
            while (true)
            {
                e = GetNextEvent();

                if (e is AssemblyLoadEvent)
                {
                    AssemblyLoadEvent ae = (AssemblyLoadEvent)e;
                    entry_point = ae.Assembly.EntryPoint;
                    if (entry_point != null)
                        break;
                }

                vm.Resume();
            }

            load_req.Disable();
        }

        [SetUp]
        public virtual void SetUp()
        {
            ThreadMirror.NativeTransitions = false;
            Start();
        }

        [TearDown]
        public void TearDown()
        {
            if (vm == null)
                return;

            if (step_req != null)
                step_req.Disable();

            vm.Resume();
            if (forceExit)
                vm.Exit(0);

            while (true)
            {
                Event e = GetNextEvent();

                if (e is VMDeathEvent)
                    break;

                vm.Resume();
            }

            vm = null;
        }

        protected abstract string ApplicationPath();

        protected abstract string SourceFilePath();

        protected Diag.ProcessStartInfo CreateStartInfo(string[] args)
        {
            var pi = new Diag.ProcessStartInfo();

            if (runtime != null)
            {
                pi.FileName = runtime;
            }
            else if (Path.DirectorySeparatorChar == '\\')
            {
                string processExe = Diag.Process.GetCurrentProcess().MainModule.FileName;
                string fileName = Path.GetFileName(processExe);
                if (fileName.StartsWith("mono") && fileName.EndsWith(".exe"))
                    pi.FileName = processExe;
            }

            if (string.IsNullOrEmpty(pi.FileName))
                pi.FileName = @"C:\projects\mono\mono\build\builds\monodistribution\bin-x64\mono.exe";
            pi.Arguments = ApplicationPath() + " " + string.Join(" ", args);
            pi.UseShellExecute = false;
            pi.CreateNoWindow = true;
            return pi;
        }

        public Event GetNextEvent()
        {
            var es = vm.GetNextEventSet();
            Assert.AreEqual(1, es.Events.Length);
            return es[0];
        }

        public BreakpointEvent run_until(string name)
        {
            // String
            MethodMirror m = entry_point.DeclaringType.GetMethod(name);
            Assert.IsNotNull(m);

            //Console.WriteLine ("X: " + name + " " + m.ILOffsets.Count + " " + m.Locations.Count);
            var req = vm.SetBreakpoint(m, m.ILOffsets[0]);

            Event e = null;

            while (true)
            {
                vm.Resume();
                e = GetNextEvent();
                if (e is BreakpointEvent)
                    break;
            }

            req.Disable();

            Assert.IsInstanceOf(typeof(BreakpointEvent), e);
            Assert.AreEqual(m.Name, (e as BreakpointEvent).Method.Name);

            return (e as BreakpointEvent);
        }

        // Assert we have stepped to a location
        protected void assert_location(Event e, string method)
        {
            Assert.IsTrue(e is StepEvent);
            Assert.AreEqual(method, (e as StepEvent).Method.Name);
        }

        public bool CheckPosition(string guid, int offset = 0, string statement = null, bool silent = false, ITextFile file = null)
        {
            file = file ?? SourceFile;

            int i = file.Text.IndexOf("/*" + guid + "*/", StringComparison.Ordinal);
            if (i == -1)
            {
                if (!silent)
                    Assert.Fail("CheckPosition failure: Guid marker not found:" + guid + " in file:" + file.Name);
                return false;
            }

            file.GetLineColumnFromPosition(i, out var line, out _);
            if (line + offset != lastStoppedPosition.Line)
            {
                if (!silent)
                    Assert.Fail("CheckPosition failure: Wrong line Expected:" + (line + offset) + " Actual:" + lastStoppedPosition.Line + " in file:" + file.Name);
                return false;
            }

            if (!string.IsNullOrEmpty(statement))
            {
                int position = file.GetPositionFromLineColumn(lastStoppedPosition.Line, lastStoppedPosition.Column);
                string actualStatement = file.GetText(position, position + statement.Length);
                if (statement != actualStatement)
                {
                    if (!silent)
                        Assert.AreEqual(statement, actualStatement);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads file from given path
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <returns></returns>
        public static ITextFile ReadFile(string sourcePath)
        {
            return new TextFile(MDTextFile.ReadFile(sourcePath));
        }
    }
}
