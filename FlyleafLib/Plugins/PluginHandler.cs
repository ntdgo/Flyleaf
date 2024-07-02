using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins;

public class PluginHandler
{
    #region Properties
    public Config                   Config                          { get; private set; }
    public bool                     Interrupt                       { get; set; }
    public IOpen                    OpenedPlugin                    { get; private set; }
    public IOpenSubtitles           OpenedSubtitlesPlugin           { get; private set; }
    public long                     OpenCounter                     { get; internal set; }
    public long                     OpenItemCounter                 { get; internal set; }
    public Playlist                 Playlist                        { get; set; }
    public int                      UniqueId                        { get; set; }

    public Dictionary<string, PluginBase>
                                    Plugins                         { get; private set; }
    public Dictionary<string, IOpen>
                                    PluginsOpen                     { get; private set; }
    public Dictionary<string, IOpenSubtitles>
                                    PluginsOpenSubtitles            { get; private set; }

    public Dictionary<string, IScrapeItem>
                                    PluginsScrapeItem               { get; private set; }

    public Dictionary<string, ISuggestPlaylistItem>
                                    PluginsSuggestItem              { get; private set; }

    public Dictionary<string, ISuggestAudioStream>
                                    PluginsSuggestAudioStream       { get; private set; }
    public Dictionary<string, ISuggestVideoStream>
                                    PluginsSuggestVideoStream       { get; private set; }
    public Dictionary<string, ISuggestExternalAudio>
                                    PluginsSuggestExternalAudio     { get; private set; }
    public Dictionary<string, ISuggestExternalVideo>
                                    PluginsSuggestExternalVideo     { get; private set; }
    #endregion

    #region Initialize
    LogHandler Log;
    public PluginHandler(Config config, int uniqueId = -1)
    {
        Config = config;
        UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
        Playlist = new Playlist(UniqueId);
        Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + " [PluginHandler ] ");
        LoadPlugins();
    }

    public static PluginBase CreatePluginInstance(PluginType type, PluginHandler handler = null)
    {
        PluginBase plugin = (PluginBase) Activator.CreateInstance(type.Type, true);
        plugin.Handler  = handler;
        plugin.Name     = type.Name;
        plugin.Type     = type.Type;
        plugin.Version  = type.Version;

        if (handler != null)
            plugin.OnLoaded();

        return plugin;
    }
    private void LoadPlugins()
    {
        Plugins = new Dictionary<string, PluginBase>();

        foreach (var type in Engine.Plugins.Types.Values)
        {
            try
            {
                var plugin = CreatePluginInstance(type, this);
                plugin.Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [{plugin.Name,-14}] ");
                Plugins.Add(plugin.Name, plugin);
            } catch (Exception e) { Log.Error($"[Plugins] [Error] Failed to load plugin ... ({e.Message} {Utils.GetRecInnerException(e)}"); }
            }

        PluginsOpen                     = new Dictionary<string, IOpen>();
        PluginsOpenSubtitles            = new Dictionary<string, IOpenSubtitles>();
        PluginsScrapeItem               = new Dictionary<string, IScrapeItem>();

        PluginsSuggestItem              = new Dictionary<string, ISuggestPlaylistItem>();

        PluginsSuggestAudioStream       = new Dictionary<string, ISuggestAudioStream>();
        PluginsSuggestVideoStream       = new Dictionary<string, ISuggestVideoStream>();

        PluginsSuggestExternalAudio     = new Dictionary<string, ISuggestExternalAudio>();
        PluginsSuggestExternalVideo     = new Dictionary<string, ISuggestExternalVideo>();

        foreach (var plugin in Plugins.Values)
            LoadPluginInterfaces(plugin);
    }
    private void LoadPluginInterfaces(PluginBase plugin)
    {
        if (plugin is IOpen) PluginsOpen.Add(plugin.Name, (IOpen)plugin);
        else if (plugin is IOpenSubtitles) PluginsOpenSubtitles.Add(plugin.Name, (IOpenSubtitles)plugin);

        if (plugin is IScrapeItem) PluginsScrapeItem.Add(plugin.Name, (IScrapeItem)plugin);

        if (plugin is ISuggestPlaylistItem) PluginsSuggestItem.Add(plugin.Name, (ISuggestPlaylistItem)plugin);

        if (plugin is ISuggestAudioStream) PluginsSuggestAudioStream.Add(plugin.Name, (ISuggestAudioStream)plugin);
        if (plugin is ISuggestVideoStream) PluginsSuggestVideoStream.Add(plugin.Name, (ISuggestVideoStream)plugin);

        if (plugin is ISuggestExternalAudio) PluginsSuggestExternalAudio.Add(plugin.Name, (ISuggestExternalAudio)plugin);
        if (plugin is ISuggestExternalVideo) PluginsSuggestExternalVideo.Add(plugin.Name, (ISuggestExternalVideo)plugin);
    }
    #endregion

    #region Misc / Events
    public void OnInitializing()
    {
        OpenCounter++;
        OpenItemCounter++;
        foreach(var plugin in Plugins.Values)
            plugin.OnInitializing();
    }
    public void OnInitialized()
    {
        OpenedPlugin            = null;
        OpenedSubtitlesPlugin   = null;

        Playlist.Reset();

        foreach(var plugin in Plugins.Values)
            plugin.OnInitialized();
    }

    public void OnInitializingSwitch()
    {
        OpenItemCounter++;
        foreach(var plugin in Plugins.Values)
            plugin.OnInitializingSwitch();
    }
    public void OnInitializedSwitch()
    {
        foreach(var plugin in Plugins.Values)
            plugin.OnInitializedSwitch();
    }

    public void Dispose()
    {
        foreach(var plugin in Plugins.Values)
            plugin.Dispose();
    }
    #endregion

    #region Audio / Video
    public OpenResults Open()
    {
        long sessionId = OpenCounter;
        var plugins = PluginsOpen.Values.OrderBy(x => x.Priority);
        foreach(var plugin in plugins)
        {
            if (Interrupt || sessionId != OpenCounter)
                return new OpenResults("Cancelled");

            if (!plugin.CanOpen())
                continue;

            var res = plugin.Open();
            if (res == null)
                continue;

            // Currently fallback is not allowed if an error has been returned
            if (res.Error != null)
                return res;

            OpenedPlugin = plugin;
            Log.Info($"[{plugin.Name}] Open Success");

            return res;
        }

        return new OpenResults("No plugin found for the provided input");
    }
    public OpenResults OpenItem()
    {
        long sessionId = OpenItemCounter;
        var res = OpenedPlugin.OpenItem();

        res ??= new OpenResults { Error = "Cancelled" };

        if (sessionId != OpenItemCounter)
            res.Error = "Cancelled";

        if (res.Error == null)
            Log.Info($"[{OpenedPlugin.Name}] Open Item ({Playlist.Selected.Index}) Success");

        return res;
    }

    // Should only be called from opened plugin
    public void OnPlaylistCompleted()
    {
        Playlist.Completed = true;
        if (Playlist.ExpectingItems == 0)
            Playlist.ExpectingItems = Playlist.Items.Count;

        if (Playlist.Items.Count > 1)
        {
            Log.Debug("Playlist Completed");
            Playlist.UpdatePrevNextItem();
        }
            
    }

    public void ScrapeItem(PlaylistItem item)
    {
        var plugins = PluginsScrapeItem.Values.OrderBy(x => x.Priority);
        foreach (var plugin in plugins)
        {
            if (Interrupt)
                return;

            plugin.ScrapeItem(item);
        }
    }

    public PlaylistItem SuggestItem()
    {
        var plugins = PluginsSuggestItem.Values.OrderBy(x => x.Priority);
        foreach (var plugin in plugins)
        {
            if (Interrupt)
                return null;

            var item = plugin.SuggestItem();
            if (item != null)
            {
                Log.Info($"SuggestItem #{item.Index} - {item.Title}");
                return item;
            }
        }

        return null;
    }

    public ExternalVideoStream SuggestExternalVideo()
    {
        var plugins = PluginsSuggestExternalVideo.Values.OrderBy(x => x.Priority);
        foreach (var plugin in plugins)
        {
            if (Interrupt)
                return null;

            var extStream = plugin.SuggestExternalVideo();
            if (extStream != null)
            {
                Log.Info($"SuggestVideo (External) {extStream.Width} x {extStream.Height} @ {extStream.FPS}");
                return extStream;
            }
        }

        return null;
    }
    public ExternalAudioStream SuggestExternalAudio()
    {
        var plugins = PluginsSuggestExternalAudio.Values.OrderBy(x => x.Priority);
        foreach(var plugin in plugins)
        {
            if (Interrupt)
                return null;

            var extStream = plugin.SuggestExternalAudio();
            if (extStream != null)
            {
                Log.Info($"SuggestAudio (External) {extStream.SampleRate} Hz, {extStream.Codec}");
                return extStream;
            }
        }

        return null;
    }

    public VideoStream SuggestVideo(ObservableCollection<VideoStream> streams)
    {
        if (streams == null || streams.Count == 0) return null;

        var plugins = PluginsSuggestVideoStream.Values.OrderBy(x => x.Priority);
        foreach(var plugin in plugins)
        {
            if (Interrupt)
                return null;

            var stream = plugin.SuggestVideo(streams);
            if (stream != null) return stream;
        }

        return null;
    }
    public void SuggestVideo(out VideoStream stream, out ExternalVideoStream extStream, ObservableCollection<VideoStream> streams)
    {
        stream = null;
        extStream = null;

        if (Interrupt)
            return;

        stream = SuggestVideo(streams);
        if (stream != null)
            return;

        if (Interrupt)
            return;

        extStream = SuggestExternalVideo();
    }

    public AudioStream SuggestAudio(ObservableCollection<AudioStream> streams)
    {
        if (streams == null || streams.Count == 0) return null;

        var plugins = PluginsSuggestAudioStream.Values.OrderBy(x => x.Priority);
        foreach(var plugin in plugins)
        {
            if (Interrupt)
                return null;

            var stream = plugin.SuggestAudio(streams);
            if (stream != null) return stream;
        }

        return null;
    }
    public void SuggestAudio(out AudioStream stream, out ExternalAudioStream extStream, ObservableCollection<AudioStream> streams)
    {
        stream = null;
        extStream = null;

        if (Interrupt)
            return;

        stream = SuggestAudio(streams);
        if (stream != null)
            return;

        if (Interrupt)
            return;

        extStream = SuggestExternalAudio();
    }
    #endregion

    #region Data
    public void SuggestData(out DataStream stream, ObservableCollection<DataStream> streams)
    {
        stream = null;

        if (Interrupt)
            return;

        stream = streams.FirstOrDefault();
    }
    #endregion
}