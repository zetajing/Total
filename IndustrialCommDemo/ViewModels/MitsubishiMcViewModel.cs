using System;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mc;

namespace IndustrialCommDemo.ViewModels
{
    /// <summary>
    /// ViewModel for the Mitsubishi MC protocol tab.
    /// </summary>
    internal sealed class MitsubishiMcViewModel : ProtocolTabViewModel
    {
        public MitsubishiMcViewModel(DemoAppContext ctx) : base(ctx) { }

        protected override string ProtocolTag => "MC";
        protected override ProtocolKind ProtocolKind => ProtocolKind.MitsubishiMc;

        protected override IIndustrialClient CreateClient()
        {
            return new MitsubishiMcClient(
                new MitsubishiMcClientOptions
                {
                    DeviceId = ParseHelper.RequireText(DeviceId, "MC 设备 ID"),
                    Host = ParseHelper.RequireText(Host, "MC 主机"),
                    Port = ParseHelper.ParseIntValue(PortOrRack, "MC 端口"),
                },
                Ctx.SdkLogger);
        }

        protected override ReadRequest BuildReadRequest()
        {
            return new ReadRequest(
                ParseHelper.RequireText(DeviceId, "设备 ID"),
                ParseHelper.RequireText(Address, "地址"),
                DataTypeFromCombo(),
                ParseHelper.ParseUShortValue(Length, "长度"));
        }

        protected override WriteRequest BuildWriteRequest()
        {
            var dt = DataTypeFromCombo();
            var len = ParseHelper.ParseUShortValue(Length, "长度");
            return new WriteRequest(
                ParseHelper.RequireText(DeviceId, "设备 ID"),
                ParseHelper.RequireText(Address, "地址"),
                dt,
                ParseHelper.ParseValue(WriteValue, dt, len),
                len);
        }

        protected override void RememberCurrentAddress()
        {
            if (!string.IsNullOrWhiteSpace(Address))
            {
                AddressHistoryHelper.RememberRecentAddress(Ctx.UiState.Mc.RecentAddresses, Address);
                RecentAddressChanged?.Invoke();
            }
        }

        public event Action RecentAddressChanged;

        public System.Collections.Generic.IReadOnlyCollection<string> RecentAddresses =>
            Ctx.UiState.Mc.RecentAddresses;

        // ── State persistence ──

        public override void SaveState()
        {
            Ctx.UiState.Mc.DeviceId = DeviceId;
            Ctx.UiState.Mc.Host = Host;
            Ctx.UiState.Mc.PortOrRack = PortOrRack;
            Ctx.UiState.Mc.Address = Address;
            Ctx.UiState.Mc.DataType = SelectedDataType.ToString();
            Ctx.UiState.Mc.Length = Length;
            Ctx.UiState.Mc.WriteValue = WriteValue;
        }

        public override void RestoreState()
        {
            var s = Ctx.UiState.Mc;
            if (s == null) return;
            if (!string.IsNullOrWhiteSpace(s.DeviceId)) DeviceId = s.DeviceId;
            if (!string.IsNullOrWhiteSpace(s.Host)) Host = s.Host;
            if (!string.IsNullOrWhiteSpace(s.PortOrRack)) PortOrRack = s.PortOrRack;
            if (!string.IsNullOrWhiteSpace(s.Address)) Address = s.Address;
            if (Enum.TryParse(s.DataType, out DataType selectedDataType)) SelectedDataType = selectedDataType;
            if (!string.IsNullOrWhiteSpace(s.Length)) Length = s.Length;
            if (!string.IsNullOrWhiteSpace(s.WriteValue)) WriteValue = s.WriteValue;
        }

        private DataType DataTypeFromCombo()
        {
            return SelectedDataType;
        }
    }
}
