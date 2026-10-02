using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace InduLinkDemo.Views
{
    public partial class VirtualPlcTab : UserControl
    {
        public VirtualPlcTab() { InitializeComponent(); }
        public void Initialize(DemoAppContext context)
        {
            S7ServerControl.Initialize(context);
            ModbusServerControl.Initialize(context, false);
            OpcUaServerControl.Initialize(context, true);
        }
        public async Task ResetAsync()
        {
            var failures = new List<Exception>();
            foreach (var stop in new Func<Task>[] { S7ServerControl.ResetSnap7ServerAsync, ModbusServerControl.ResetAsync, OpcUaServerControl.ResetAsync })
            {
                try { await stop(); }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (failures.Count != 0) throw new AggregateException("Some virtual services could not be stopped.", failures);
        }
    }
}
