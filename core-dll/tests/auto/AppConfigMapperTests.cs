using System;
using System.Reflection;
using BouyomiVoicepeakBridge.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class AppConfigMapperTests
{
    [TestMethod]
    public void Map_DebugLogTextCandidates_IsIgnored()
    {
        AppConfigData source = new AppConfigData();
        source.Debug.LogTextCandidates = true;

        object mapped = InvokeMap(source);
        object debug = mapped.GetType().GetProperty("Debug")?.GetValue(mapped);
        PropertyInfo property = debug?.GetType().GetProperty("LogTextCandidates");

        Assert.IsNull(property);
    }

    private static object InvokeMap(AppConfigData source)
    {
        Type mapperType = Type.GetType("VoicepeakProxyWorker.AppConfigMapper, VoicepeakProxyWorker", throwOnError: true);
        MethodInfo map = mapperType.GetMethod("Map", BindingFlags.Static | BindingFlags.Public);
        return map.Invoke(null, new object[] { source });
    }
}
