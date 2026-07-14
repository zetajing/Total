using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;

namespace IndustrialCommDemo.Views
{
    public partial class ModbusTab
    {
        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyProfile();
            RefreshDataTypeState();
        }

        private void ApplyProfile()
        {
            _profile = GetSelectedProfile();
            _addressParser = new ModbusAddressParser(_profile);
            ExampleAddressTextBlock.Text = _profile.ExampleAddresses;
            if (string.IsNullOrWhiteSpace(AddressTextBox.Text))
                AddressTextBox.Text = _profile.DefaultAddress;
        }

        private IModbusDeviceProfile GetSelectedProfile()
        {
            var key = (ModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(key)) return ModbusDeviceProfiles.Generic;
            return ModbusDeviceProfiles.All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? ModbusDeviceProfiles.Generic;
        }

        private void RefreshProfileOptions()
        {
            if (ModelComboBox == null) return;
            var selectedKey = (ModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var isRtu = ConnectionTypeComboBox.SelectedIndex == 1;
            ModelComboBox.Items.Clear();
            if (isRtu)
            {
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "通用 Modbus", Tag = ModbusDeviceProfiles.Generic.Key });
            }
            else
            {
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "汇川 EasyPLC", Tag = ModbusDeviceProfiles.InovanceEasyPlc.Key });
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "三菱 Modbus TCP", Tag = ModbusDeviceProfiles.MitsubishiModbusTcp.Key });
            }
            SelectModel(selectedKey);
        }

        private void SelectModel(string modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey)) { ModelComboBox.SelectedIndex = 0; return; }
            foreach (var item in ModelComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, modelKey, StringComparison.OrdinalIgnoreCase))
                { ModelComboBox.SelectedItem = item; return; }
            }
            ModelComboBox.SelectedIndex = 0;
        }

        private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; RefreshDataTypeState(); RefreshInputHints(); }
        private void DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!IsLoaded) return; RefreshLengthSuggestion(); RefreshInputHints(); }
        private void WriteValueTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; RefreshLengthSuggestion(); RefreshInputHints(); }
        private void AddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { ComboHelper.ApplyHistorySelection(AddressHistoryComboBox, AddressTextBox); }

        private void RefreshDataTypeState()
        {
            var addresses = ParseHelper.SplitAddresses(AddressTextBox.Text);
            var isBitAddress = addresses.Count > 0 && addresses.All(IsModbusBitAddress);
            if (isBitAddress)
            {
                ComboHelper.SetEnabledDataTypes(DataTypeComboBox, new[] { DataType.Bool });
                ComboHelper.SelectDataType(DataTypeComboBox, DataType.Bool);
            }
            else
            {
                ComboHelper.SetEnabledDataTypes(DataTypeComboBox, RegisterDataTypes);
                if (ComboHelper.GetSelectedDataType(DataTypeComboBox) == DataType.Bool)
                    ComboHelper.SelectDataType(DataTypeComboBox, DataType.Int16);
            }
            RefreshLengthSuggestion();
        }

        private void RefreshInputHints()
        {
            var analysis = AnalyzeAddresses();
            if (!analysis.IsValid)
            {
                AddressHintTextBlock.Foreground = Brushes.OrangeRed;
                AddressHintTextBlock.Text = analysis.ErrorMessage;
            }
            else if (analysis.AddressCount == 0)
            {
                AddressHintTextBlock.Foreground = Brushes.DimGray;
                AddressHintTextBlock.Text = "支持逗号、分号、换行输入多个地址。";
            }
            else
            {
                AddressHintTextBlock.Foreground = Brushes.ForestGreen;
                var modeText = analysis.AddressCount == 1 ? "单地址" : string.Format(CultureInfo.InvariantCulture, "多地址（{0} 个）", analysis.AddressCount);
                AddressHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0}，{1}。{2}", modeText,
                    analysis.IsBitFamily ? "位地址模式，只允许 Bool 解析" : "寄存器地址模式，可选择值的解析方式",
                    analysis.AddressCount > 1 ? "读取、订阅、写入都支持批量。" : "可直接读取、写入或订阅。");
            }
            RefreshWriteHint(analysis);
        }

        private void RefreshWriteHint(ModbusAddressInputAnalysis analysis)
        {
            if (!analysis.IsValid || analysis.AddressCount == 0)
            {
                WriteHintTextBlock.Foreground = Brushes.DimGray;
                WriteHintTextBlock.Text = "多地址写入时，可填写 1 个值广播，或填写与地址数相同的多个值。";
                return;
            }
            if (analysis.AddressCount == 1)
            {
                WriteHintTextBlock.Foreground = Brushes.DimGray;
                WriteHintTextBlock.Text = "单地址写入沿用当前解析类型和长度。";
                return;
            }
            var values = ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text);
            if (values.Count <= 1)
            {
                WriteHintTextBlock.Foreground = Brushes.ForestGreen;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将把同一个值写到 {0} 个地址。", analysis.AddressCount);
            }
            else if (values.Count == analysis.AddressCount)
            {
                WriteHintTextBlock.Foreground = Brushes.ForestGreen;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将按顺序逐项写入 {0} 个地址。", analysis.AddressCount);
            }
            else
            {
                WriteHintTextBlock.Foreground = Brushes.OrangeRed;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                    "多地址写入时，写入值数量必须是 1 个或 {0} 个，当前为 {1} 个。", analysis.AddressCount, values.Count);
            }
        }

        private void RefreshLengthSuggestion()
        {
            if (IsModbusBitAddress(AddressTextBox.Text)) { LengthTextBox.Text = "1"; return; }
            try
            {
                var len = GetSuggestedLength(ComboHelper.GetSelectedDataType(DataTypeComboBox), WriteValueTextBox.Text);
                LengthTextBox.Text = len.ToString(CultureInfo.InvariantCulture);
            }
            catch { LengthTextBox.Text = "1"; }
        }

        private static ushort GetSuggestedLength(DataType dataType, string textValue)
        {
            return IndustrialCommSdk.Protocols.Common.RegisterValueCodec.GetRequiredRegisterLength(dataType, textValue);
        }

        private List<ReadRequest> BuildReadRequests()
        {
            var addresses = ValidateAndGetAddresses();
            var deviceId = GetDeviceId();
            var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
            var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "Modbus 长度");
            return addresses.Select(a => new ReadRequest(deviceId, a, dataType, length)).ToList();
        }

        private List<WriteRequest> BuildWriteRequests()
        {
            var addresses = ValidateAndGetAddresses();
            var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
            var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "Modbus 长度");
            var deviceId = GetDeviceId();

            if (addresses.Count == 1)
            {
                return new List<WriteRequest>
                {
                    new WriteRequest(deviceId, addresses[0], dataType,
                        ParseHelper.ParseValue(WriteValueTextBox.Text, dataType, length), length)
                };
            }

            var values = ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text);
            if (values.Count == 0) throw new InvalidOperationException("Modbus 写入值不能为空。");
            if (values.Count != 1 && values.Count != addresses.Count)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "多地址写入时，写入值数量必须是 1 个或与地址数量一致（{0} 个）。", addresses.Count));

            if (values.Count == 1)
            {
                var parsed = ParseHelper.ParseValue(values[0], dataType, length);
                return addresses.Select(a => new WriteRequest(deviceId, a, dataType, parsed, length)).ToList();
            }

            return addresses
                .Zip(values, (a, vt) => new WriteRequest(deviceId, a, dataType, ParseHelper.ParseValue(vt, dataType, length), length))
                .ToList();
        }

        private bool IsSingleWriteValueBroadcast(int addressCount)
        {
            return addressCount > 1 && ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text).Count <= 1;
        }

        private static bool IsModbusBitAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            switch (char.ToUpperInvariant(address.Trim()[0]))
            {
                case 'X': case 'Y': case 'M': case 'S': case 'B': return true;
                default: return false;
            }
        }

        private List<string> ValidateAndGetAddresses()
        {
            var analysis = AnalyzeAddresses();
            if (!analysis.IsValid) throw new InvalidOperationException(analysis.ErrorMessage);
            return analysis.Addresses;
        }

        private ModbusAddressInputAnalysis AnalyzeAddresses()
        {
            var addresses = ParseHelper.SplitAddresses(AddressTextBox.Text);
            if (addresses.Count == 0) return ModbusAddressInputAnalysis.Empty();

            var parsedAreas = new List<ModbusArea>(addresses.Count);
            foreach (var address in addresses)
            {
                try
                {
                    var parsed = (ModbusAddress)_addressParser.Parse(address);
                    parsedAreas.Add(parsed.Area);
                }
                catch
                {
                    return ModbusAddressInputAnalysis.Invalid(addresses, "存在无法识别的 Modbus 地址，请检查前缀和编号。");
                }
            }

            var hasBit = parsedAreas.Any(a => a == ModbusArea.Coil || a == ModbusArea.DiscreteInput);
            var hasReg = parsedAreas.Any(a => !(a == ModbusArea.Coil || a == ModbusArea.DiscreteInput));
            if (hasBit && hasReg)
                return ModbusAddressInputAnalysis.Invalid(addresses, "多地址模式下，位地址和寄存器地址不能混用。");

            return ModbusAddressInputAnalysis.Valid(addresses, hasBit);
        }
    }
}
