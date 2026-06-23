using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IndustrialCommDemo.Services
{
    internal sealed class UiStateStore
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(DemoUiState));
        private readonly string _filePath;

        public UiStateStore()
        {
            var baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndustrialCommDemo");
            Directory.CreateDirectory(baseDirectory);
            _filePath = Path.Combine(baseDirectory, "ui-state.json");
        }

        public DemoUiState Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Normalize(new DemoUiState());
                }

                using (var stream = File.OpenRead(_filePath))
                {
                    return Normalize((DemoUiState)Serializer.ReadObject(stream) ?? new DemoUiState());
                }
            }
            catch
            {
                return Normalize(new DemoUiState());
            }
        }

        public void Save(DemoUiState state)
        {
            if (state == null)
            {
                return;
            }

            state = Normalize(state);
            using (var stream = File.Create(_filePath))
            {
                Serializer.WriteObject(stream, state);
            }
        }

        private static DemoUiState Normalize(DemoUiState state)
        {
            state.Modbus = state.Modbus ?? new ModbusUiState();
            state.Socket = state.Socket ?? new SocketUiState();
            state.S7 = state.S7 ?? new ProtocolUiState();
            state.Mc = state.Mc ?? new ProtocolUiState();

            state.Modbus.RecentAddresses = state.Modbus.RecentAddresses ?? new List<string>();
            state.S7.RecentAddresses = state.S7.RecentAddresses ?? new List<string>();
            state.Mc.RecentAddresses = state.Mc.RecentAddresses ?? new List<string>();
            return state;
        }
    }

    [DataContract]
    internal sealed class DemoUiState
    {
        [DataMember(Order = 1)]
        public ModbusUiState Modbus { get; set; } = new ModbusUiState();

        [DataMember(Order = 2)]
        public SocketUiState Socket { get; set; } = new SocketUiState();

        [DataMember(Order = 3)]
        public ProtocolUiState S7 { get; set; } = new ProtocolUiState();

        [DataMember(Order = 4)]
        public ProtocolUiState Mc { get; set; } = new ProtocolUiState();
    }

    [DataContract]
    internal sealed class ModbusUiState
    {
        [DataMember(Order = 1)]
        public string DeviceId { get; set; }

        [DataMember(Order = 2)]
        public string Host { get; set; }

        [DataMember(Order = 3)]
        public string Port { get; set; }

        [DataMember(Order = 4)]
        public string SlaveId { get; set; }

        [DataMember(Order = 5)]
        public string Address { get; set; }

        [DataMember(Order = 6)]
        public string Length { get; set; }

        [DataMember(Order = 7)]
        public string WriteValue { get; set; }

        [DataMember(Order = 8)]
        public string PollInterval { get; set; }

        [DataMember(Order = 9)]
        public List<string> RecentAddresses { get; set; } = new List<string>();
    }

    [DataContract]
    internal sealed class SocketUiState
    {
        [DataMember(Order = 1)]
        public string ServerIp { get; set; }

        [DataMember(Order = 2)]
        public string ServerPort { get; set; }

        [DataMember(Order = 3)]
        public string ClientHost { get; set; }

        [DataMember(Order = 4)]
        public string ClientPort { get; set; }

        [DataMember(Order = 5)]
        public bool EchoEnabled { get; set; } = true;

        [DataMember(Order = 6)]
        public string ServerMessage { get; set; }

        [DataMember(Order = 7)]
        public string ClientMessage { get; set; }
    }

    [DataContract]
    internal sealed class ProtocolUiState
    {
        [DataMember(Order = 1)]
        public string DeviceId { get; set; }

        [DataMember(Order = 2)]
        public string Host { get; set; }

        [DataMember(Order = 3)]
        public string PortOrRack { get; set; }

        [DataMember(Order = 4)]
        public string SlotOrLength { get; set; }

        [DataMember(Order = 5)]
        public string Address { get; set; }

        [DataMember(Order = 6)]
        public string Length { get; set; }

        [DataMember(Order = 7)]
        public string WriteValue { get; set; }

        [DataMember(Order = 8)]
        public List<string> RecentAddresses { get; set; } = new List<string>();
    }
}
