using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Diagnostics;
using InduLink.FileTransfer.Ftp;
using InduLink.Protocols.Mqtt;
using InduLink.Runtime;
using InduLink.Runtime.Security;
using InduLink.Web.Gateway;

namespace InduLinkDemo.Services
{
    /// <summary>
    /// 统一管理桌面应用内的网络入口和 FTP 客户端。配置只保存密钥名称，实际凭据由
    /// Windows DPAPI CurrentUser 密钥库保存。
    /// </summary>
    public sealed class NetworkServicesRuntime : IDisposable
    {
        private const string MqttRootTopic = "industrial/v1";

        private readonly IndustrialApplicationRuntime _applicationRuntime;
        private readonly NetworkServicesConfigurationStore _configurationStore;
        private readonly DpapiSecretStore _secretStore;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposeSource = new CancellationTokenSource();
        private readonly object _configurationSync = new object();

        private NetworkServicesConfiguration _configuration;
        private IMqttBrokerService _mqttBroker;
        private IMqttTagGatewayBridge _mqttBridge;
        private X509Certificate2 _mqttCertificate;
        private IIndustrialWebGateway _webGateway;
        private IFtpFileClient _ftpClient;
        private bool _mqttDesired;
        private bool _webDesired;
        private int _gatewayGeneration;
        private int _suppressGatewayRefresh;
        private int _disposed;

        public NetworkServicesRuntime(
            IndustrialApplicationRuntime applicationRuntime,
            IIndustrialLogger logger = null)
            : this(applicationRuntime, null, logger)
        {
        }

        public NetworkServicesRuntime(
            IndustrialApplicationRuntime applicationRuntime,
            string configurationFilePath,
            IIndustrialLogger logger = null)
        {
            _applicationRuntime = applicationRuntime ?? throw new ArgumentNullException(nameof(applicationRuntime));
            _logger = logger ?? NullIndustrialLogger.Instance;
            _configurationStore = new NetworkServicesConfigurationStore(configurationFilePath);
            _secretStore = new DpapiSecretStore(_configurationStore.SecretsDirectory);
            _configuration = CloneConfiguration(_configurationStore.Load());
            _applicationRuntime.TagGatewayChanged += ApplicationRuntimeOnTagGatewayChanged;
        }

        public string ConfigurationFilePath { get { return _configurationStore.FilePath; } }
        public string SecretsDirectory { get { return _configurationStore.SecretsDirectory; } }

        /// <summary>返回配置快照；修改后需调用 SaveConfigurationAsync 才会生效。</summary>
        public NetworkServicesConfiguration Configuration
        {
            get
            {
                lock (_configurationSync) return CloneConfiguration(_configuration);
            }
        }

        public IMqttBrokerService MqttBroker { get { return _mqttBroker; } }
        public IMqttTagGatewayBridge MqttBridge { get { return _mqttBridge; } }
        public IIndustrialWebGateway WebGateway { get { return _webGateway; } }
        public IFtpFileClient FtpClient { get { return _ftpClient; } }
        public bool IsMqttRunning { get { return _mqttBroker != null && _mqttBroker.IsRunning; } }
        public bool IsWebGatewayRunning { get { return _webGateway != null && _webGateway.IsRunning; } }
        public bool IsFtpConnected { get { return _ftpClient != null && _ftpClient.IsConnected; } }

        public event EventHandler<NetworkServiceStateChangedEventArgs> StateChanged;

        /// <summary>
        /// 只启动配置中明确设置 autoStart=true 的服务。FTP 配置没有自动连接开关，
        /// 因此始终需要用户显式连接。
        /// </summary>
        public async Task StartAutoServicesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var configuration = Configuration;
            if (configuration.MqttBroker.AutoStart)
                await StartMqttAsync(cancellationToken).ConfigureAwait(false);
            if (configuration.WebGateway.AutoStart)
                await StartWebGatewayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ReloadConfigurationAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var loaded = CloneConfiguration(_configurationStore.Load());
                lock (_configurationSync) _configuration = loaded;
                ApplyGatewayOptions(_applicationRuntime.TagGateway, loaded.WebGateway);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task SaveConfigurationAsync(
            NetworkServicesConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var snapshot = CloneConfiguration(configuration);
            NetworkServicesConfigurationStore.Validate(snapshot);

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _configurationStore.Save(snapshot);
                lock (_configurationSync) _configuration = CloneConfiguration(snapshot);
                ApplyGatewayOptions(_applicationRuntime.TagGateway, snapshot.WebGateway);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public void SetSecret(string secretName, string value)
        {
            ThrowIfDisposed();
            _secretStore.Set(secretName, value);
        }

        public bool TryGetSecret(string secretName, out string value)
        {
            ThrowIfDisposed();
            return _secretStore.TryGet(secretName, out value);
        }

        public bool RemoveSecret(string secretName)
        {
            ThrowIfDisposed();
            return _secretStore.Remove(secretName);
        }

        public async Task StartMqttAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _mqttDesired = true;
                try
                {
                    await StartMqttCoreAsync(Configuration, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _mqttDesired = false;
                    RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Faulted, "MQTT 服务启动失败。");
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopMqttAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _mqttDesired = false;
                await StopMqttCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StartWebGatewayAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _webDesired = true;
                try
                {
                    await StartWebGatewayCoreAsync(Configuration, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _webDesired = false;
                    RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Faulted, "WebAPI/WebSocket 服务启动失败。");
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopWebGatewayAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _webDesired = false;
                await StopWebGatewayCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task ConnectFtpAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_ftpClient != null && _ftpClient.IsConnected) return;
                await DisconnectFtpCoreAsync(CancellationToken.None).ConfigureAwait(false);

                var configuration = Configuration;
                NetworkServicesConfigurationStore.Validate(configuration);
                var ftp = configuration.Ftp;
                var password = GetRequiredSecret(ftp.PasswordSecretName, "FTP password");
                var options = new FtpClientOptions
                {
                    Host = ftp.Host,
                    Port = ftp.Port,
                    Username = ftp.Username,
                    Password = password,
                    RootPath = ftp.RemoteRoot,
                    SecurityMode = ftp.UseTls ? FtpSecurityMode.ExplicitTls : FtpSecurityMode.Plain,
                    AllowInsecureFtp = ftp.AllowInsecureFtp,
                    DataConnectionMode = ftp.PassiveMode
                        ? FtpDataConnectionMode.Passive
                        : FtpDataConnectionMode.Active,
                };

                RaiseState(NetworkServiceKind.FtpClient, NetworkServiceState.Starting, "正在连接 FTP/FTPS。");
                var client = new FtpFileClient(options, _logger);
                try
                {
                    await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    _ftpClient = client;
                    RaiseState(NetworkServiceKind.FtpClient, NetworkServiceState.Running, "FTP/FTPS 已连接。");
                }
                catch
                {
                    client.Dispose();
                    RaiseState(NetworkServiceKind.FtpClient, NetworkServiceState.Faulted, "FTP/FTPS 连接失败。");
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task DisconnectFtpAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DisconnectFtpCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _mqttDesired = false;
                _webDesired = false;
                await StopAllCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StartMqttCoreAsync(
            NetworkServicesConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (_mqttBroker != null && _mqttBroker.IsRunning && _mqttBridge != null && _mqttBridge.IsRunning)
                return;

            await StopMqttCoreAsync(CancellationToken.None).ConfigureAwait(false);
            NetworkServicesConfigurationStore.Validate(configuration);
            await EnsureApplicationLoadedAsync(cancellationToken).ConfigureAwait(false);
            var gateway = _applicationRuntime.TagGateway;
            if (gateway == null) throw new InvalidOperationException("Industrial Tag gateway is not available.");
            ApplyGatewayOptions(gateway, configuration.WebGateway);

            var mqtt = configuration.MqttBroker;
            var password = GetRequiredSecret(mqtt.PasswordSecretName, "MQTT broker password");
            X509Certificate2 certificate = null;
            if (mqtt.UseTls)
                certificate = FindServerCertificate(mqtt.CertificateThumbprint);

            var options = new MqttBrokerOptions
            {
                BindAddress = mqtt.BindAddress,
                Port = mqtt.Port,
                UseTls = mqtt.UseTls,
                TlsPort = mqtt.TlsPort,
                ServerCertificate = certificate,
                AllowAnonymous = false,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [mqtt.Username] = password,
                },
                PublishAuthorizer = (username, clientId, topic) =>
                    MqttTagGatewayBridge.IsClientPublishAllowed(MqttRootTopic, clientId, topic),
                SubscribeAuthorizer = (username, clientId, topicFilter) =>
                    MqttTagGatewayBridge.IsClientSubscriptionAllowed(MqttRootTopic, clientId, topicFilter),
            };

            RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Starting, "正在启动 MQTT Broker。");
            var broker = new MqttBrokerService(options, _logger);
            var bridge = new MqttTagGatewayBridge(
                broker,
                gateway,
                new MqttTagGatewayOptions { RootTopic = MqttRootTopic },
                _logger);
            try
            {
                await broker.StartAsync(cancellationToken).ConfigureAwait(false);
                await bridge.StartAsync(cancellationToken).ConfigureAwait(false);
                _mqttCertificate = certificate;
                certificate = null;
                _mqttBroker = broker;
                _mqttBridge = bridge;
                RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Running, "MQTT Broker 与 Tag 网关已启动。");
            }
            catch
            {
                try { await bridge.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                bridge.Dispose();
                try { await broker.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                broker.Dispose();
                throw;
            }
            finally
            {
                if (certificate != null) certificate.Dispose();
            }
        }

        private async Task StartWebGatewayCoreAsync(
            NetworkServicesConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (_webGateway != null && _webGateway.IsRunning) return;
            await StopWebGatewayCoreAsync(CancellationToken.None).ConfigureAwait(false);
            NetworkServicesConfigurationStore.Validate(configuration);
            await EnsureApplicationLoadedAsync(cancellationToken).ConfigureAwait(false);
            var gateway = _applicationRuntime.TagGateway;
            if (gateway == null) throw new InvalidOperationException("Industrial Tag gateway is not available.");
            ApplyGatewayOptions(gateway, configuration.WebGateway);

            var web = configuration.WebGateway;
            var apiKey = GetRequiredSecret(web.ApiKeySecretName, "Web API key");
            var options = new IndustrialWebGatewayOptions
            {
                ListenPrefix = web.ListenPrefix,
                RequireApiKey = true,
                ApiKey = apiKey,
            };
            foreach (var origin in web.AllowedOrigins ?? Enumerable.Empty<string>())
                options.AllowedOrigins.Add(origin);

            RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Starting, "正在启动 WebAPI/WebSocket 网关。");
            var service = new IndustrialWebGateway(gateway, options, _logger);
            try
            {
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
                _webGateway = service;
                RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Running, "WebAPI/WebSocket 网关已启动。");
            }
            catch
            {
                try { await service.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                service.Dispose();
                throw;
            }
        }

        private async Task StopMqttCoreAsync(CancellationToken cancellationToken)
        {
            var bridge = _mqttBridge;
            var broker = _mqttBroker;
            var certificate = _mqttCertificate;
            _mqttBridge = null;
            _mqttBroker = null;
            _mqttCertificate = null;
            if (bridge == null && broker == null && certificate == null) return;

            RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Stopping, "正在停止 MQTT 服务。");
            Exception failure = null;
            if (bridge != null)
            {
                try { await bridge.StopAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { failure = ex; }
                finally { bridge.Dispose(); }
            }
            if (broker != null)
            {
                try { await broker.StopAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { if (failure == null) failure = ex; }
                finally { broker.Dispose(); }
            }
            if (certificate != null) certificate.Dispose();
            RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Stopped, "MQTT 服务已停止。");
            if (failure != null) throw failure;
        }

        private async Task StopWebGatewayCoreAsync(CancellationToken cancellationToken)
        {
            var service = _webGateway;
            _webGateway = null;
            if (service == null) return;
            RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Stopping, "正在停止 WebAPI/WebSocket 网关。");
            try
            {
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                service.Dispose();
                RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Stopped, "WebAPI/WebSocket 网关已停止。");
            }
        }

        private async Task DisconnectFtpCoreAsync(CancellationToken cancellationToken)
        {
            var client = _ftpClient;
            _ftpClient = null;
            if (client == null) return;
            RaiseState(NetworkServiceKind.FtpClient, NetworkServiceState.Stopping, "正在断开 FTP/FTPS。");
            try
            {
                await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                client.Dispose();
                RaiseState(NetworkServiceKind.FtpClient, NetworkServiceState.Stopped, "FTP/FTPS 已断开。");
            }
        }

        private async Task StopAllCoreAsync(CancellationToken cancellationToken)
        {
            Exception failure = null;
            try { await StopWebGatewayCoreAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { failure = ex; }
            try { await StopMqttCoreAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { if (failure == null) failure = ex; }
            try { await DisconnectFtpCoreAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { if (failure == null) failure = ex; }
            if (failure != null) throw failure;
        }

        private void ApplicationRuntimeOnTagGatewayChanged(object sender, EventArgs e)
        {
            // StartMqtt/StartWeb may create the host for the first time. That event is already handled by
            // the in-progress start and must not enqueue a redundant stop/start cycle.
            if (Volatile.Read(ref _suppressGatewayRefresh) != 0) return;
            var generation = Interlocked.Increment(ref _gatewayGeneration);
            _ = Task.Run(() => RefreshForTagGatewayChangeAsync(generation));
        }

        private async Task RefreshForTagGatewayChangeAsync(int generation)
        {
            try
            {
                await _lifecycleGate.WaitAsync(_disposeSource.Token).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _gatewayGeneration))
                        return;

                    var restartMqtt = _mqttDesired;
                    var restartWeb = _webDesired;
                    try { await StopWebGatewayCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { _logger.Warn("Web gateway did not stop cleanly after a Tag gateway change."); }
                    try { await StopMqttCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { _logger.Warn("MQTT service did not stop cleanly after a Tag gateway change."); }
                    if (_applicationRuntime.TagGateway == null) return;

                    var configuration = Configuration;
                    if (restartMqtt)
                    {
                        try { await StartMqttCoreAsync(configuration, _disposeSource.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (_disposeSource.IsCancellationRequested) { return; }
                        catch
                        {
                            RaiseState(NetworkServiceKind.MqttBroker, NetworkServiceState.Faulted,
                                "设备配置变化后 MQTT 服务重新启动失败。");
                            _logger.Warn("MQTT service restart after Tag gateway change failed.");
                        }
                    }
                    if (restartWeb)
                    {
                        try { await StartWebGatewayCoreAsync(configuration, _disposeSource.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (_disposeSource.IsCancellationRequested) { return; }
                        catch
                        {
                            RaiseState(NetworkServiceKind.WebGateway, NetworkServiceState.Faulted,
                                "设备配置变化后 Web 网关重新启动失败。");
                            _logger.Warn("Web gateway restart after Tag gateway change failed.");
                        }
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }
            catch (OperationCanceledException) when (_disposeSource.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
                _logger.Warn("Network services refresh after Tag gateway change failed.");
            }
        }

        private async Task EnsureApplicationLoadedAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _suppressGatewayRefresh);
            try
            {
                await _applicationRuntime.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _suppressGatewayRefresh);
            }
        }

        private string GetRequiredSecret(string secretName, string description)
        {
            string value;
            if (!_secretStore.TryGet(secretName, out value) || string.IsNullOrEmpty(value))
                throw new InvalidOperationException(description + " is not configured in the protected secret store.");
            return value;
        }

        private static void ApplyGatewayOptions(
            IIndustrialTagGateway gateway,
            WebGatewayConfiguration configuration)
        {
            if (gateway == null || configuration == null) return;
            gateway.Options.EnableRemoteWrites = configuration.EnableRemoteWrites;
            gateway.Options.AllowRawAddressReads = configuration.AllowRawAddressReads;
            gateway.Options.ExposeRawAddresses = configuration.ExposeRawAddresses;
        }

        private static X509Certificate2 FindServerCertificate(string thumbprint)
        {
            var normalized = NormalizeThumbprint(thumbprint);
            if (string.IsNullOrEmpty(normalized))
                throw new InvalidOperationException("MQTT TLS requires a certificate thumbprint.");

            Exception lastFailure = null;
            foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                try
                {
                    using (var store = new X509Store(StoreName.My, location))
                    {
                        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                        foreach (var certificate in store.Certificates)
                        {
                            if (!string.Equals(NormalizeThumbprint(certificate.Thumbprint), normalized,
                                StringComparison.OrdinalIgnoreCase)) continue;
                            if (!certificate.HasPrivateKey)
                                throw new InvalidOperationException("The configured MQTT TLS certificate has no private key.");
                            return new X509Certificate2(certificate);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is CryptographicException || ex is UnauthorizedAccessException)
                {
                    lastFailure = ex;
                }
            }

            if (lastFailure != null)
                throw new InvalidOperationException("The Windows certificate store could not be read.", lastFailure);
            throw new InvalidOperationException("The configured MQTT TLS certificate was not found in a Windows My store.");
        }

        private static string NormalizeThumbprint(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        }

        private void RaiseState(NetworkServiceKind service, NetworkServiceState state, string message)
        {
            var handlers = StateChanged;
            if (handlers == null) return;
            var args = new NetworkServiceStateChangedEventArgs(service, state, message, DateTimeOffset.UtcNow);
            foreach (EventHandler<NetworkServiceStateChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, args); }
                catch { _logger.Warn("A network service state event handler failed."); }
            }
        }

        private static NetworkServicesConfiguration CloneConfiguration(NetworkServicesConfiguration source)
        {
            source = source ?? new NetworkServicesConfiguration();
            var mqtt = source.MqttBroker ?? new MqttBrokerConfiguration();
            var web = source.WebGateway ?? new WebGatewayConfiguration();
            var ftp = source.Ftp ?? new FtpConnectionConfiguration();
            return new NetworkServicesConfiguration
            {
                Version = source.Version,
                MqttBroker = new MqttBrokerConfiguration
                {
                    AutoStart = mqtt.AutoStart,
                    BindAddress = mqtt.BindAddress,
                    Port = mqtt.Port,
                    UseTls = mqtt.UseTls,
                    TlsPort = mqtt.TlsPort,
                    CertificateThumbprint = mqtt.CertificateThumbprint,
                    Username = mqtt.Username,
                    PasswordSecretName = mqtt.PasswordSecretName,
                },
                WebGateway = new WebGatewayConfiguration
                {
                    AutoStart = web.AutoStart,
                    ListenPrefix = web.ListenPrefix,
                    ApiKeySecretName = web.ApiKeySecretName,
                    EnableRemoteWrites = web.EnableRemoteWrites,
                    AllowRawAddressReads = web.AllowRawAddressReads,
                    ExposeRawAddresses = web.ExposeRawAddresses,
                    AllowedOrigins = (web.AllowedOrigins ?? new List<string>()).ToList(),
                },
                Ftp = new FtpConnectionConfiguration
                {
                    Host = ftp.Host,
                    Port = ftp.Port,
                    Username = ftp.Username,
                    PasswordSecretName = ftp.PasswordSecretName,
                    UseTls = ftp.UseTls,
                    AllowInsecureFtp = ftp.AllowInsecureFtp,
                    PassiveMode = ftp.PassiveMode,
                    RemoteRoot = ftp.RemoteRoot,
                },
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _applicationRuntime.TagGatewayChanged -= ApplicationRuntimeOnTagGatewayChanged;
            _disposeSource.Cancel();
            _lifecycleGate.Wait();
            try
            {
                _mqttDesired = false;
                _webDesired = false;
                try { StopAllCoreAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch { _logger.Warn("One or more network services did not stop cleanly during disposal."); }
                _secretStore.Dispose();
            }
            finally
            {
                _lifecycleGate.Release();
                _lifecycleGate.Dispose();
                _disposeSource.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(NetworkServicesRuntime));
        }
    }

    public enum NetworkServiceKind
    {
        MqttBroker,
        WebGateway,
        FtpClient,
    }

    public enum NetworkServiceState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Faulted,
    }

    public sealed class NetworkServiceStateChangedEventArgs : EventArgs
    {
        public NetworkServiceStateChangedEventArgs(
            NetworkServiceKind service,
            NetworkServiceState state,
            string message,
            DateTimeOffset timestampUtc)
        {
            Service = service;
            State = state;
            Message = message;
            TimestampUtc = timestampUtc;
        }

        public NetworkServiceKind Service { get; }
        public NetworkServiceState State { get; }
        public string Message { get; }
        public DateTimeOffset TimestampUtc { get; }
    }
}
