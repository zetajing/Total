using System;
using InduLink.Protocols.Ads.Router;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class AdsRouterTests
    {
        [Test]
        public void ValidRouterOptionsAreAccepted()
        {
            var options = new AdsRouterOptions
            {
                Name = "TestRouter",
                NetId = "192.168.1.20.1.1",
                TcpPort = 48898,
                LoopbackPort = 48898,
            };
            options.RemoteConnections.Add(new AdsRemoteRouteOptions
            {
                Name = "VirtualPlc",
                Address = "192.168.1.90",
                NetId = "192.168.1.90.1.1",
                Type = "TCP_IP",
            });

            Assert.DoesNotThrow(() => AdsRouterConfigurationValidator.Validate(options));
        }

        [Test]
        public void RouterOptionsRejectMissingNameOrInvalidNetId()
        {
            Assert.Throws<InvalidOperationException>(() =>
                AdsRouterConfigurationValidator.Validate(new AdsRouterOptions
                {
                    NetId = "192.168.1.20.1.1",
                }));

            Assert.Throws<InvalidOperationException>(() =>
                AdsRouterConfigurationValidator.Validate(new AdsRouterOptions
                {
                    Name = "TestRouter",
                    NetId = "192.168.1.20",
                }));
        }

        [Test]
        public void RouterOptionsRejectInvalidPortAndRouteType()
        {
            var invalidPort = new AdsRouterOptions
            {
                Name = "TestRouter",
                NetId = "192.168.1.20.1.1",
                TcpPort = 70000,
            };
            Assert.Throws<InvalidOperationException>(() =>
                AdsRouterConfigurationValidator.Validate(invalidPort));

            var invalidRoute = new AdsRouterOptions
            {
                Name = "TestRouter",
                NetId = "192.168.1.20.1.1",
            };
            invalidRoute.RemoteConnections.Add(new AdsRemoteRouteOptions
            {
                Name = "VirtualPlc",
                Address = "192.168.1.90",
                NetId = "192.168.1.90.1.1",
                Type = "UDP",
            });
            Assert.Throws<InvalidOperationException>(() =>
                AdsRouterConfigurationValidator.Validate(invalidRoute));
        }
    }
}
