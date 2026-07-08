using System;
using System.Threading;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.S7;
using CpuType = S7.Net.CpuType;

namespace IndustrialCommDemo.ViewModels
{
    /// <summary>
    /// ViewModel for the Siemens S7 protocol tab.
    /// Handles address parsing (TIA portal style + standard addresses)
    /// and wraps the S7-specific client creation.
    /// </summary>
    internal sealed class SiemensS7ViewModel : ProtocolTabViewModel
    {
        public SiemensS7ViewModel(DemoAppContext ctx) : base(ctx) { }

        protected override string ProtocolTag => "S7";
        protected override ProtocolKind ProtocolKind => ProtocolKind.SiemensS7;
        protected override IIndustrialClient CreateClient()
        {
            return new SiemensS7Client(
                new SiemensS7ClientOptions
                {
                    DeviceId = ParseHelper.RequireText(DeviceId, "S7 设备 ID"),
                    Host = ParseHelper.RequireText(Host, "S7 主机"),
                    Rack = ParseHelper.ParseShortValue(PortOrRack, "S7 机架"),
                    Slot = ParseHelper.ParseShortValue(SlotOrLength, "S7 槽位"),
                    CpuType = CpuType.S71200,
                },
                Ctx.SdkLogger);
        }

        protected override ReadRequest BuildReadRequest()
        {
            var typed = ParseS7AddressInput(Address);
            var dataType = typed.InferredDataType ?? DataTypeFromCombo();
            var length = typed.InferredLength ?? ParseHelper.ParseUShortValue(Length, "长度");
            return new ReadRequest(
                ParseHelper.RequireText(DeviceId, "设备 ID"),
                typed.NormalizedAddress, dataType, length);
        }

        protected override WriteRequest BuildWriteRequest()
        {
            var typed = ParseS7AddressInput(Address);
            var dataType = typed.InferredDataType ?? DataTypeFromCombo();
            var length = typed.InferredLength ?? ParseHelper.ParseUShortValue(Length, "长度");
            return new WriteRequest(
                ParseHelper.RequireText(DeviceId, "设备 ID"),
                typed.NormalizedAddress, dataType,
                ParseHelper.ParseValue(WriteValue, dataType, length), length);
        }

        protected override void RememberCurrentAddress()
        {
            if (!string.IsNullOrWhiteSpace(Address))
            {
                AddressHistoryHelper.RememberRecentAddress(Ctx.UiState.S7.RecentAddresses, Address);
                RecentAddressChanged?.Invoke();
            }
        }

        /// <summary>
        /// Fired when the recent-address list changes. The view subscribes to refresh the combo.
        /// </summary>
        public event Action RecentAddressChanged;

        public System.Collections.Generic.IReadOnlyCollection<string> RecentAddresses =>
            Ctx.UiState.S7.RecentAddresses;

        // ── State persistence ──

        public override void SaveState()
        {
            Ctx.UiState.S7.DeviceId = DeviceId;
            Ctx.UiState.S7.Host = Host;
            Ctx.UiState.S7.PortOrRack = PortOrRack;
            Ctx.UiState.S7.SlotOrLength = SlotOrLength;
            Ctx.UiState.S7.Address = Address;
            Ctx.UiState.S7.DataType = SelectedDataType.ToString();
            Ctx.UiState.S7.Length = Length;
            Ctx.UiState.S7.WriteValue = WriteValue;
        }

        public override void RestoreState()
        {
            var s = Ctx.UiState.S7;
            if (s == null) return;
            if (!string.IsNullOrWhiteSpace(s.DeviceId)) DeviceId = s.DeviceId;
            if (!string.IsNullOrWhiteSpace(s.Host)) Host = s.Host;
            if (!string.IsNullOrWhiteSpace(s.PortOrRack)) PortOrRack = s.PortOrRack;
            if (!string.IsNullOrWhiteSpace(s.SlotOrLength)) SlotOrLength = s.SlotOrLength;
            if (!string.IsNullOrWhiteSpace(s.Address)) Address = s.Address;
            if (Enum.TryParse(s.DataType, out DataType selectedDataType)) SelectedDataType = selectedDataType;
            if (!string.IsNullOrWhiteSpace(s.Length)) Length = s.Length;
            if (!string.IsNullOrWhiteSpace(s.WriteValue)) WriteValue = s.WriteValue;
        }

        // ── Data type resolution ──

        private DataType DataTypeFromCombo()
        {
            return SelectedDataType;
        }

        /// <summary>
        /// Parse address and auto-detect data type/length from TIA-style declarations.
        /// Called from the view's TextChanged handler.
        /// Returns the analysis without mutating state.
        /// </summary>
        public IndustrialCommDemo.S7AddressInputInfo AnalyzeAddress(string input)
        {
            try { return ParseS7AddressInput(input); }
            catch { return new S7AddressInputInfo(input, null, null); }
        }

        // ── S7 address parsing (moved from SiemensS7Tab.xaml.cs) ──

        internal static IndustrialCommDemo.S7AddressInputInfo ParseS7AddressInput(string input)
        {
            var text = ParseHelper.RequireText(input, "S7 地址");
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string typeToken = null;
            string addressToken = text;
            if (tokens.Length >= 2 && TryMapS7DeclarationType(tokens[0], out _, out _))
            {
                typeToken = tokens[0];
                addressToken = tokens[tokens.Length - 1];
            }
            var normalized = NormalizeS7Address(addressToken);
            if (typeToken != null && TryMapS7DeclarationType(typeToken, out var dt, out var len))
                return new IndustrialCommDemo.S7AddressInputInfo(normalized, dt, len);
            if (TryInferS7DataType(normalized, out var infDt, out var infLen))
                return new IndustrialCommDemo.S7AddressInputInfo(normalized, infDt, infLen);
            return new IndustrialCommDemo.S7AddressInputInfo(normalized, null, null);
        }

        internal static string NormalizeS7Address(string token)
        {
            var v = ParseHelper.RequireText(token, "S7 地址").Trim().ToUpperInvariant();
            if (v.StartsWith("P#", StringComparison.Ordinal)) v = v.Substring(2);
            if (v.StartsWith("%", StringComparison.Ordinal)) v = v.Substring(1);
            return v;
        }

        private static bool TryInferS7DataType(string addr, out DataType dt, out ushort len)
        {
            len = 1;
            var a = (addr ?? "").Trim().ToUpperInvariant();
            if (a.Contains(".DBX") || IsBitRef(a))
            {
                dt = DataType.Bool;
                return true;
            }
            dt = default; len = 0; return false;
        }

        private static bool IsBitRef(string v)
        {
            var u = (v ?? "").Trim().ToUpperInvariant();
            return (u.StartsWith("M") || u.StartsWith("I") || u.StartsWith("Q")) && u.Contains(".");
        }

        internal static bool TryMapS7DeclarationType(string token, out DataType dt, out ushort len)
        {
            len = 1;
            switch ((token ?? "").Trim().ToUpperInvariant())
            {
                case "BOOL": dt = DataType.Bool; return true;
                case "BYTE": dt = DataType.Byte; return true;
                case "CHAR": dt = DataType.Char; return true;
                case "INT": dt = DataType.Int16; return true;
                case "WORD": dt = DataType.UInt16; return true;
                case "DINT": dt = DataType.Int32; return true;
                case "DWORD": dt = DataType.UInt32; return true;
                case "REAL": dt = DataType.Float; return true;
                case "LREAL": dt = DataType.Double; return true;
                case "STRING": dt = DataType.String; return true;
                default: dt = default; len = 0; return false;
            }
        }
    }

    // S7AddressInputInfo is in IndustrialCommDemo namespace (Models/DisplayModels.cs)
}
