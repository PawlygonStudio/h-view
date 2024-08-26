﻿using Hai.HView.Core;
using VRC.OSCQuery;

namespace Hai.HView.OSC;

public class HQuery
{
    private const string VrchatClientPrefix = "VRChat-Client";
    
    public event VrcOscPortFound OnVrcOscPortFound;
    public delegate void VrcOscPortFound(int oscPort);
    
    private readonly int _port;
    private readonly HMessageBox _messageBox;
    private readonly int _queryPort;
    
    private OSCQueryService _ourService;
    private OSCQueryServiceProfile _vrcQueryNullable;

    public HQuery(int oscPort, int queryPort, HMessageBox messageBox)
    {
        _port = oscPort;
        _messageBox = messageBox;
        _queryPort = queryPort;
    }

    public static int RandomQueryPort()
    {
        return Extensions.GetAvailableTcpPort();
    }

    public void Start()
    {
        var qPort = _queryPort;
        _ourService = new OSCQueryServiceBuilder().WithServiceName($"{HVApp.AppName}-Overlay")
            .WithTcpPort(qPort)
            .WithUdpPort(_port)
            .WithDiscovery(new MeaModDiscovery())
            .StartHttpServer()
            .AdvertiseOSC()
            .AdvertiseOSCQuery()
            .Build();
        _ourService.AddEndpoint("/avatar/change", "s", Attributes.AccessValues.WriteOnly);
        _ourService.AddEndpoint("/avatar/parameters/VelocityX", "f", Attributes.AccessValues.WriteOnly);
        _ourService.AddEndpoint("/tracking/vrsystem", "ffffff", Attributes.AccessValues.WriteOnly);
        _ourService.OnOscQueryServiceAdded += profile =>
        {
            Console.WriteLine($"Found a query service at: {profile.name}");
            if (profile.name.StartsWith(VrchatClientPrefix))
            {
                _vrcQueryNullable = profile;
                Console.WriteLine($"VRChat Query is at http://{profile.address}:{profile.port}/");
                Task.Run(async () => await AsyncGetService(profile));
            }
        };
        _ourService.OnOscServiceAdded += profile =>
        {
            if (profile.name.StartsWith(VrchatClientPrefix))
            {
                Console.WriteLine($"VRChat OSC is at osc://{profile.address}:{profile.port}/");
                OnVrcOscPortFound?.Invoke(profile.port);
            }
        };
        _ourService.RefreshServices();
    }

    private async Task AsyncGetService(OSCQueryServiceProfile profile)
    {
        var tree = await Extensions.GetOSCTree(profile.address, profile.port);

        try
        {
            var all = Flatten(tree)
                .Where(node => node.Access != Attributes.AccessValues.NoValue)
                .ToArray();
            foreach (var parameter in all)
            {
                _messageBox.ReceivedQuery(parameter);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private IEnumerable<OSCQueryNode> Flatten(OSCQueryRootNode treeContents)
    {
        return Flatten2(treeContents.Contents.Values);
    }

    private IEnumerable<OSCQueryNode> Flatten2(Dictionary<string, OSCQueryNode>.ValueCollection nodeContents)
    {
        return nodeContents
            .Concat(nodeContents
                .Where(node => node.Contents != null && node.Contents.Values != null)
                .SelectMany(node => Flatten2(node.Contents.Values))
            );
    }

    public void Refresh()
    {
        if (_vrcQueryNullable == null) return;
        
        _messageBox.Reset();
        AsyncGetService(_vrcQueryNullable);
    }

    public void Finish()
    {
        _ourService.Dispose();
    }
}