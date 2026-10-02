using System;
using System.Reflection;
using InduLink.Mes;
using NUnit.Framework;
namespace InduLink.Tests
{
    [TestFixture]
    public sealed class MesDemoConfigurationTests
    {
        [Test]
        public void DemoReceiverConfigurationDefaultsLegacyBodyTimeoutToFiveSeconds()
        {
            var demoJsonType = Type.GetType(
                "InduLinkDemo.Helpers.MesDemoJson, InduLinkDemo",
                true);
            var createDefault = demoJsonType.GetMethod(
                "CreateDefaultReceiverConfiguration",
                BindingFlags.Static | BindingFlags.Public);
            var parse = demoJsonType.GetMethod(
                "ParseReceiverConfiguration",
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(createDefault, Is.Not.Null);
            Assert.That(parse, Is.Not.Null);

            var template = (string)createDefault.Invoke(null, null);
            Assert.That(template, Does.Contain("\"requestBodyTimeoutMilliseconds\": 5000"));

            const string legacyConfiguration =
                "{\"listenPrefix\":\"http://127.0.0.1:8081/mes/\"," +
                "\"maxConcurrentRequests\":1,\"maxRequestContentBytes\":1024," +
                "\"handlerTimeoutMilliseconds\":1000,\"responseStatusCode\":200," +
                "\"responseJson\":{}}";
            var parsed = parse.Invoke(null, new object[] { legacyConfiguration });
            var optionsProperty = parsed.GetType().GetProperty("Options");
            Assert.That(optionsProperty, Is.Not.Null);
            var parsedOptions = (MesJsonReceiverOptions)optionsProperty.GetValue(parsed, null);
            Assert.That(parsedOptions.RequestBodyTimeoutMilliseconds, Is.EqualTo(5000));
        }

    }
}
