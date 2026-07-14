using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using IndustrialCommDemo.Helpers;

namespace IndustrialCommDemo.Views
{
    public partial class DatabaseTab
    {
        public void SaveState()
        {
            _ctx.UiState.Database.ConnectionString = ConnectionStringTextBox.Text;
            _ctx.UiState.Database.TableName = TableNameTextBox.Text;
            _ctx.UiState.Database.QueryDeviceId = QueryDeviceComboBox.Text;
            _ctx.UiState.Database.QueryAddress = QueryAddressTextBox.Text;
            _ctx.UiState.Database.AddressContains = QueryContainsCheckBox.IsChecked == true;
            _ctx.UiState.Database.FromTime = QueryFromTextBox.Text;
            _ctx.UiState.Database.ToTime = QueryToTextBox.Text;
            _ctx.UiState.Database.PageSize = GetPageSize();
            var retention = RetentionComboBox.SelectedItem as ComboBoxItem;
            _ctx.UiState.Database.RetentionDays = retention == null ? 30 : int.Parse(Convert.ToString(retention.Tag), CultureInfo.InvariantCulture);
            _ctx.UiState.Database.Protocol = GetComboTagText(QueryProtocolComboBox);
            _ctx.UiState.Database.DataType = GetComboTagText(QueryDataTypeComboBox);
            _ctx.UiState.Database.Quality = GetComboTagText(QueryQualityComboBox);
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Database ?? new Services.DatabaseUiState();
            ComboHelper.SetIfNotEmpty(ConnectionStringTextBox, state.ConnectionString);
            ComboHelper.SetIfNotEmpty(TableNameTextBox, state.TableName);
            QueryDeviceComboBox.Text = state.QueryDeviceId ?? string.Empty;
            QueryAddressTextBox.Text = state.QueryAddress ?? string.Empty;
            QueryContainsCheckBox.IsChecked = state.AddressContains;
            QueryFromTextBox.Text = state.FromTime ?? string.Empty;
            QueryToTextBox.Text = state.ToTime ?? string.Empty;
            ComboHelper.SelectComboBoxByContent(QueryPageSizeComboBox, state.PageSize <= 0 ? "100" : state.PageSize.ToString(CultureInfo.InvariantCulture));
            ComboHelper.SelectComboBoxByTag(QueryProtocolComboBox, state.Protocol);
            ComboHelper.SelectComboBoxByTag(QueryDataTypeComboBox, state.DataType);
            ComboHelper.SelectComboBoxByTag(QueryQualityComboBox, state.Quality);
            foreach (var item in RetentionComboBox.Items.OfType<ComboBoxItem>())
                if (Convert.ToString(item.Tag) == (state.RetentionDays <= 0 ? 30 : state.RetentionDays).ToString(CultureInfo.InvariantCulture))
                { RetentionComboBox.SelectedItem = item; break; }
            EnabledCheckBox.IsChecked = false;
            StatusTextBlock.Text = "未启用";
        }

        private static string GetComboTagText(ComboBox combo)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item?.Tag?.ToString();
        }
    }
}
