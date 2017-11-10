using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    public interface IServerBackend
    {
        void Init(string dataDirectory);
        void Halt();

#if NET35
        //Trace listeners are registered before Init is called
        //so that errors during Init can be recorded.
        IEnumerable<System.Diagnostics.TraceSource> TraceSources();
#endif

        // These can return NULL if no backend is used.
        IJobServerV2 JobServer();
        IPalletServer PalletServer();
        IInspectionControl InspectionControl();
        ILogServerV2 LogServer();
        ICellConfiguration CellConfiguration();
    }

    public interface IBackgroundWorker
    {
        //The init function should initialize a timer or spawn a thread and then return
        void Init(IServerBackend backend);

        //Once the halt function is called, the class is garbage collected.
        //A new class will be created when the service starts again
        void Halt();

#if NET35
        System.Diagnostics.TraceSource TraceSource { get; }
#endif
    }
}
