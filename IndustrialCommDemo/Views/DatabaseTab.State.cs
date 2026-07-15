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
            SaveCurrentProviderDraft();
            _ctx.UiState.Database.Provider = _selectedProvider;
            _ctx.UiState.Database.SqlServerConnectionString = _sqlServerConnectionString;
            _ctx.UiState.Database.SqlServerTableName = _sqlServerTableName;
            _ctx.UiState.Database.MySqlConnectionString = _mySqlConnectionString;
            _ctx.UiState.Database.MySqlTableName = _mySqlTableName;
            // 保留旧字段，旧版 Demo 仍能读取 SQL Server 草稿。
            _ctx.UiState.Database.ConnectionString = _sqlServerConnectionString;
            _ctx.UiState.Database.TableName = _sqlServerTableName;
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
            _sqlServerConnectionString = FirstNonEmpty(state.SqlServerConnectionString, state.ConnectionString, DefaultSqlServerConnectionString);
            _sqlServerTableName = FirstNonEmpty(state.SqlServerTableName, state.TableName, DefaultSqlServerTableName);
            _mySqlConnectionString = FirstNonEmpty(state.MySqlConnectionString, DefaultMySqlConnectionString);
            _mySqlTableName = FirstNonEmpty(state.MySqlTableName, DefaultMySqlTableName);
            _selectedProvider = string.Equals(state.Provider, MySqlProvider, StringComparison.OrdinalIgnoreCase)
                ? MySqlProvider
                : SqlServerProvider;
            _changingProvider = true;
            ComboHelper.SelectComboBoxByTag(DatabaseProviderComboBox, _selectedProvider);
            _changingProvider = false;
            LoadSelectedProviderDraft();
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

        private void DatabaseProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_changingProvider || ConnectionStringTextBox == null || TableNameTextBox == null) return;
            SaveCurrentProviderDraft();
            var selected = DatabaseProviderComboBox.SelectedItem as ComboBoxItem;
            _selectedProvider = string.Equals(Convert.ToString(selected?.Tag), MySqlProvider, StringComparison.OrdinalIgnoreCase)
                ? MySqlProvider
                : SqlServerProvider;
            LoadSelectedProviderDraft();
        }

        private void SaveCurrentProviderDraft()
        {
            if (ConnectionStringTextBox == null || TableNameTextBox == null) return;
            if (_selectedProvider == MySqlProvider)
            {
                _mySqlConnectionString = ConnectionStringTextBox.Text;
                _mySqlTableName = TableNameTextBox.Text;
            }
            else
            {
                _sqlServerConnectionString = ConnectionStringTextBox.Text;
                _sqlServerTableName = TableNameTextBox.Text;
            }
        }

        private void LoadSelectedProviderDraft()
        {
            var isMySql = _selectedProvider == MySqlProvider;
            ConnectionStringTextBox.Text = isMySql ? _mySqlConnectionString : _sqlServerConnectionString;
            TableNameTextBox.Text = isMySql ? _mySqlTableName : _sqlServerTableName;
            ProviderHintTextBlock.Text = isMySql
                ? "MySQL 支持 table 或 database.table，要求 MySQL 8.0+。ui-state.json 为明文，生产环境不要在连接字符串中持久化密码。"
                : "SQL Server 使用 schema.table；建议使用 Windows 身份验证。";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.First(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string GetComboTagText(ComboBox combo)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item?.Tag?.ToString();
        }
    }
}
