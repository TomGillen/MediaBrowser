﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Dlna.Common;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MediaBrowser.Dlna.PlayTo
{
    public class Device : IDisposable
    {
        const string ServiceAvtransportType = "urn:schemas-upnp-org:service:AVTransport:1";
        const string ServiceRenderingType = "urn:schemas-upnp-org:service:RenderingControl:1";

        #region Fields & Properties

        private Timer _timer;

        public DeviceInfo Properties { get; set; }

        private int _muteVol;
        public bool IsMuted
        {
            get
            {
                return _muteVol > 0;
            }
        }

        private string _currentId = String.Empty;
        public string CurrentId
        {
            get
            {
                return _currentId;
            }
            set
            {
                if (_currentId == value)
                    return;
                _currentId = value;

                NotifyCurrentIdChanged(value);
            }
        }

        public int Volume { get; set; }

        public TimeSpan Duration { get; set; }

        private TimeSpan _position = TimeSpan.FromSeconds(0);
        public TimeSpan Position
        {
            get
            {
                return _position;
            }
            set
            {
                _position = value;
            }
        }

        private TRANSPORTSTATE _transportState = TRANSPORTSTATE.STOPPED;
        public TRANSPORTSTATE TransportState
        {
            get
            {
                return _transportState;
            }
            set
            {
                if (_transportState == value)
                    return;

                _transportState = value;

                NotifyPlaybackChanged(value);
            }
        }

        public bool IsPlaying
        {
            get
            {
                return TransportState == TRANSPORTSTATE.PLAYING;
            }
        }

        public bool IsTransitioning
        {
            get
            {
                return (TransportState == TRANSPORTSTATE.TRANSITIONING);
            }
        }

        public bool IsPaused
        {
            get
            {
                return TransportState == TRANSPORTSTATE.PAUSED || TransportState == TRANSPORTSTATE.PAUSED_PLAYBACK;
            }
        }

        public bool IsStopped
        {
            get
            {
                return TransportState == TRANSPORTSTATE.STOPPED;
            }
        }

        public DateTime UpdateTime { get; private set; }

        #endregion

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _config;

        public Device(DeviceInfo deviceProperties, IHttpClient httpClient, ILogger logger, IServerConfigurationManager config)
        {
            Properties = deviceProperties;
            _httpClient = httpClient;
            _logger = logger;
            _config = config;
        }

        private int GetPlaybackTimerIntervalMs()
        {
            return 2000;
        }

        private int GetInactiveTimerIntervalMs()
        {
            return 20000;
        }

        public void Start()
        {
            UpdateTime = DateTime.UtcNow;

            var interval = GetPlaybackTimerIntervalMs();

            _timer = new Timer(TimerCallback, null, interval, interval);
        }

        private void RestartTimer()
        {
            var interval = GetPlaybackTimerIntervalMs();

            _timer.Change(interval, interval);
        }


        /// <summary>
        /// Restarts the timer in inactive mode.
        /// </summary>
        private void RestartTimerInactive()
        {
            var interval = GetInactiveTimerIntervalMs();

            _timer.Change(interval, interval);
        }

        private void StopTimer()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        #region Commanding

        public Task<bool> VolumeDown(bool mute = false)
        {
            var sendVolume = (Volume - 5) > 0 ? Volume - 5 : 0;
            if (mute && _muteVol == 0)
            {
                sendVolume = 0;
                _muteVol = Volume;
            }
            return SetVolume(sendVolume);
        }

        public Task<bool> VolumeUp(bool unmute = false)
        {
            var sendVolume = (Volume + 5) < 100 ? Volume + 5 : 100;
            if (unmute && _muteVol > 0)
                sendVolume = _muteVol;
            _muteVol = 0;
            return SetVolume(sendVolume);
        }

        public Task ToggleMute()
        {
            if (_muteVol == 0)
            {
                _muteVol = Volume;
                return SetVolume(0);
            }

            var tmp = _muteVol;
            _muteVol = 0;
            return SetVolume(tmp);
        }

        /// <summary>
        /// Sets volume on a scale of 0-100
        /// </summary>
        public async Task<bool> SetVolume(int value)
        {
            var command = RendererCommands.ServiceActions.FirstOrDefault(c => c.Name == "SetVolume");
            if (command == null)
                return true;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceRenderingType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, RendererCommands.BuildPost(command, service.ServiceType, value))
                .ConfigureAwait(false);
            Volume = value;
            return true;
        }

        public async Task<TimeSpan> Seek(TimeSpan value)
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "Seek");
            if (command == null)
                return value;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, String.Format("{0:hh}:{0:mm}:{0:ss}", value), "REL_TIME"))
                .ConfigureAwait(false);

            return value;
        }

        public async Task<bool> SetAvTransport(string url, string header, string metaData)
        {
            StopTimer();

            await SetStop().ConfigureAwait(false);
            CurrentId = null;

            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "SetAVTransportURI");
            if (command == null)
                return false;

            var dictionary = new Dictionary<string, string>
            {
                {"CurrentURI", url},
                {"CurrentURIMetaData", CreateDidlMeta(metaData)}
            };

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, url, dictionary), header)
                .ConfigureAwait(false);


            await Task.Delay(50).ConfigureAwait(false);
            await SetPlay().ConfigureAwait(false);


            _lapsCount = GetLapsCount();
            RestartTimer();

            return true;
        }

        private string CreateDidlMeta(string value)
        {
            if (value == null)
                return String.Empty;

            var escapedData = value.Replace("<", "&lt;").Replace(">", "&gt;");

            return String.Format(BaseDidl, escapedData.Replace("\r\n", ""));
        }

        private const string BaseDidl = "&lt;DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\" xmlns:dlna=\"urn:schemas-dlna-org:metadata-1-0/\"&gt;{0}&lt;/DIDL-Lite&gt;";

        public async Task<bool> SetNextAvTransport(string value, string header, string metaData)
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "SetNextAVTransportURI");
            if (command == null)
                return false;

            var dictionary = new Dictionary<string, string>
            {
                {"NextURI", value},
                {"NextURIMetaData", CreateDidlMeta(metaData)}
            };

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, value, dictionary), header)
                .ConfigureAwait(false);

            await Task.Delay(100).ConfigureAwait(false);

            return true;
        }

        public async Task<bool> SetPlay()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "Play");
            if (command == null)
                return false;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, 1))
                .ConfigureAwait(false);

            _lapsCount = GetLapsCount();
            return true;
        }

        public async Task<bool> SetStop()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "Stop");
            if (command == null)
                return false;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, 1))
                .ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> SetPause()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "Pause");
            if (command == null)
                return false;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType, 1))
                .ConfigureAwait(false);

            await Task.Delay(50).ConfigureAwait(false);
            TransportState = TRANSPORTSTATE.PAUSED_PLAYBACK;
            return true;
        }

        #endregion

        #region Get data

        private int GetLapsCount()
        {
            // No need to get all data every lap, just every X time. 
            return 10;
        }

        int _lapsCount = 0;

        private async void TimerCallback(object sender)
        {
            if (_disposed)
                return;

            StopTimer();

            try
            {
                await GetTransportInfo().ConfigureAwait(false);

                //If we're not playing anything no need to get additional data
                if (TransportState != TRANSPORTSTATE.STOPPED)
                {
                    var hasTrack = await GetPositionInfo().ConfigureAwait(false);

                    // TODO: Why make these requests if hasTrack==false?
                    // TODO ANSWER Some vendors don't include track in GetPositionInfo, use GetMediaInfo instead.
                    if (_lapsCount > GetLapsCount())
                    {
                        if (!hasTrack)
                        {
                            await GetMediaInfo().ConfigureAwait(false);
                        }
                        await GetVolume().ConfigureAwait(false);
                        _lapsCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error updating device info", ex);
            }

            _lapsCount++;

            if (_disposed)
                return;

            //If we're not playing anything make sure we don't get data more often than neccessry to keep the Session alive
            if (TransportState != TRANSPORTSTATE.STOPPED)
                RestartTimer();
            else
                RestartTimerInactive();
        }

        private async Task GetVolume()
        {
            var command = RendererCommands.ServiceActions.FirstOrDefault(c => c.Name == "GetVolume");
            if (command == null)
                return;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceRenderingType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, RendererCommands.BuildPost(command, service.ServiceType))
                .ConfigureAwait(false);

            if (result == null || result.Document == null)
                return;

            var volume = result.Document.Descendants(uPnpNamespaces.RenderingControl + "GetVolumeResponse").Select(i => i.Element("CurrentVolume")).FirstOrDefault(i => i != null);
            var volumeValue = volume == null ? null : volume.Value;

            if (string.IsNullOrWhiteSpace(volumeValue))
                return;

            Volume = int.Parse(volumeValue, UsCulture);

            //Reset the Mute value if Volume is bigger than zero
            if (Volume > 0 && _muteVol > 0)
            {
                _muteVol = 0;
            }
        }

        private async Task GetTransportInfo()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "GetTransportInfo");
            if (command == null)
                return;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);
            if (service == null)
                return;

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, AvCommands.BuildPost(command, service.ServiceType))
                .ConfigureAwait(false);

            if (result == null || result.Document == null)
                return;

            var transportState =
                result.Document.Descendants(uPnpNamespaces.AvTransport + "GetTransportInfoResponse").Select(i => i.Element("CurrentTransportState")).FirstOrDefault(i => i != null);

            var transportStateValue = transportState == null ? null : transportState.Value;

            if (transportStateValue != null)
            {
                TRANSPORTSTATE state;

                if (Enum.TryParse(transportStateValue, true, out state))
                {
                    TransportState = state;
                }
            }

            UpdateTime = DateTime.UtcNow;
        }

        private async Task GetMediaInfo()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "GetMediaInfo");
            if (command == null)
                return;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, RendererCommands.BuildPost(command, service.ServiceType))
                .ConfigureAwait(false);

            if (result == null || result.Document == null)
                return;

            var track = result.Document.Descendants("CurrentURIMetaData").FirstOrDefault();

            if (track == null)
            {
                CurrentId = null;
                return;
            }

            var e = track.Element(uPnpNamespaces.items) ?? track;

            var uTrack = uParser.CreateObjectFromXML(new uParserObject
            {
                Type = e.GetValue(uPnpNamespaces.uClass),
                Element = e
            });

            if (uTrack != null)
                CurrentId = uTrack.Id;
        }

        private async Task<bool> GetPositionInfo()
        {
            var command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == "GetPositionInfo");
            if (command == null)
                return true;

            var service = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);

            if (service == null)
            {
                throw new InvalidOperationException("Unable to find service");
            }

            var result = await new SsdpHttpClient(_httpClient, _config).SendCommandAsync(Properties.BaseUrl, service, command.Name, RendererCommands.BuildPost(command, service.ServiceType))
                .ConfigureAwait(false);

            if (result == null || result.Document == null)
                return true;

            var durationElem = result.Document.Descendants(uPnpNamespaces.AvTransport + "GetPositionInfoResponse").Select(i => i.Element("TrackDuration")).FirstOrDefault(i => i != null);
            var duration = durationElem == null ? null : durationElem.Value;

            if (!string.IsNullOrWhiteSpace(duration) && !string.Equals(duration, "NOT_IMPLEMENTED", StringComparison.OrdinalIgnoreCase))
            {
                Duration = TimeSpan.Parse(duration, UsCulture);
            }

            var positionElem = result.Document.Descendants(uPnpNamespaces.AvTransport + "GetPositionInfoResponse").Select(i => i.Element("RelTime")).FirstOrDefault(i => i != null);
            var position = positionElem == null ? null : positionElem.Value;

            if (!string.IsNullOrWhiteSpace(position) && !string.Equals(position, "NOT_IMPLEMENTED", StringComparison.OrdinalIgnoreCase))
            {
                Position = TimeSpan.Parse(position, UsCulture);
            }

            var track = result.Document.Descendants("TrackMetaData").FirstOrDefault();

            if (track == null)
            {
                //If track is null, some vendors do this, use GetMediaInfo instead                    
                return false;
            }

            var trackString = (string)track;

            if (string.IsNullOrWhiteSpace(trackString) || string.Equals(trackString, "NOT_IMPLEMENTED", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            XElement uPnpResponse;

            try
            {
                uPnpResponse = XElement.Parse(trackString);
            }
            catch
            {
                _logger.Error("Unable to parse xml {0}", trackString);
                return false;
            }

            var e = uPnpResponse.Element(uPnpNamespaces.items);

            var uTrack = CreateUBaseObject(e);

            if (uTrack == null)
                return true;

            CurrentId = uTrack.Id;

            return true;
        }

        private static uBaseObject CreateUBaseObject(XElement container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            return new uBaseObject
            {
                Id = container.GetAttributeValue(uPnpNamespaces.Id),
                ParentId = container.GetAttributeValue(uPnpNamespaces.ParentId),
                Title = container.GetValue(uPnpNamespaces.title),
                IconUrl = container.GetValue(uPnpNamespaces.Artwork),
                SecondText = "",
                Url = container.GetValue(uPnpNamespaces.Res),
                ProtocolInfo = GetProtocolInfo(container),
                MetaData = container.ToString()
            };
        }

        private static string[] GetProtocolInfo(XElement container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            var resElement = container.Element(uPnpNamespaces.Res);

            if (resElement != null)
            {
                var info = resElement.Attribute(uPnpNamespaces.ProtocolInfo);

                if (info != null && !string.IsNullOrWhiteSpace(info.Value))
                {
                    return info.Value.Split(':');
                }
            }

            return new string[4];
        }

        #endregion

        #region From XML

        private async Task GetAVProtocolAsync()
        {
            var avService = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceAvtransportType);
            if (avService == null)
                return;

            var url = avService.ScpdUrl;
            if (!url.Contains("/"))
                url = "/dmr/" + url;
            if (!url.StartsWith("/"))
                url = "/" + url;

            var httpClient = new SsdpHttpClient(_httpClient, _config);
            var document = await httpClient.GetDataAsync(Properties.BaseUrl + url);

            AvCommands = TransportCommands.Create(document);
        }

        private async Task GetRenderingProtocolAsync()
        {
            var avService = Properties.Services.FirstOrDefault(s => s.ServiceType == ServiceRenderingType);

            if (avService == null)
                return;
            string url = avService.ScpdUrl;
            if (!url.Contains("/"))
                url = "/dmr/" + url;
            if (!url.StartsWith("/"))
                url = "/" + url;

            var httpClient = new SsdpHttpClient(_httpClient, _config);
            var document = await httpClient.GetDataAsync(Properties.BaseUrl + url);

            RendererCommands = TransportCommands.Create(document);
        }

        private TransportCommands AvCommands
        {
            get;
            set;
        }

        internal TransportCommands RendererCommands
        {
            get;
            set;
        }

        public static async Task<Device> CreateuPnpDeviceAsync(Uri url, IHttpClient httpClient, IServerConfigurationManager config, ILogger logger)
        {
            var ssdpHttpClient = new SsdpHttpClient(httpClient, config);

            var document = await ssdpHttpClient.GetDataAsync(url.ToString()).ConfigureAwait(false);

            var deviceProperties = new DeviceInfo();

            var name = document.Descendants(uPnpNamespaces.ud.GetName("friendlyName")).FirstOrDefault();
            if (name != null)
                deviceProperties.Name = name.Value;

            var name2 = document.Descendants(uPnpNamespaces.ud.GetName("roomName")).FirstOrDefault();
            if (name2 != null)
                deviceProperties.Name = name2.Value;

            var model = document.Descendants(uPnpNamespaces.ud.GetName("modelName")).FirstOrDefault();
            if (model != null)
                deviceProperties.ModelName = model.Value;

            var modelNumber = document.Descendants(uPnpNamespaces.ud.GetName("modelNumber")).FirstOrDefault();
            if (modelNumber != null)
                deviceProperties.ModelNumber = modelNumber.Value;

            var uuid = document.Descendants(uPnpNamespaces.ud.GetName("UDN")).FirstOrDefault();
            if (uuid != null)
                deviceProperties.UUID = uuid.Value;

            var manufacturer = document.Descendants(uPnpNamespaces.ud.GetName("manufacturer")).FirstOrDefault();
            if (manufacturer != null)
                deviceProperties.Manufacturer = manufacturer.Value;

            var manufacturerUrl = document.Descendants(uPnpNamespaces.ud.GetName("manufacturerURL")).FirstOrDefault();
            if (manufacturerUrl != null)
                deviceProperties.ManufacturerUrl = manufacturerUrl.Value;

            var presentationUrl = document.Descendants(uPnpNamespaces.ud.GetName("presentationURL")).FirstOrDefault();
            if (presentationUrl != null)
                deviceProperties.PresentationUrl = presentationUrl.Value;

            var modelUrl = document.Descendants(uPnpNamespaces.ud.GetName("modelURL")).FirstOrDefault();
            if (modelUrl != null)
                deviceProperties.ModelUrl = modelUrl.Value;

            var serialNumber = document.Descendants(uPnpNamespaces.ud.GetName("serialNumber")).FirstOrDefault();
            if (serialNumber != null)
                deviceProperties.SerialNumber = serialNumber.Value;

            var modelDescription = document.Descendants(uPnpNamespaces.ud.GetName("modelDescription")).FirstOrDefault();
            if (modelDescription != null)
                deviceProperties.ModelDescription = modelDescription.Value;

            deviceProperties.BaseUrl = String.Format("http://{0}:{1}", url.Host, url.Port);

            var icon = document.Descendants(uPnpNamespaces.ud.GetName("icon")).FirstOrDefault();

            if (icon != null)
            {
                deviceProperties.Icon = CreateIcon(icon);
            }

            var isRenderer = false;

            foreach (var services in document.Descendants(uPnpNamespaces.ud.GetName("serviceList")))
            {
                if (services == null)
                    return null;

                var servicesList = services.Descendants(uPnpNamespaces.ud.GetName("service"));

                if (servicesList == null)
                    return null;

                foreach (var element in servicesList)
                {
                    var service = Create(element);

                    if (service != null)
                    {
                        deviceProperties.Services.Add(service);
                        if (service.ServiceType == ServiceAvtransportType)
                        {
                            isRenderer = true;
                        }
                    }
                }
            }

            if (isRenderer)
            {
                var device = new Device(deviceProperties, httpClient, logger, config);

                await device.GetRenderingProtocolAsync().ConfigureAwait(false);
                await device.GetAVProtocolAsync().ConfigureAwait(false);

                return device;
            }

            return null;
        }

        #endregion

        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");
        private static DeviceIcon CreateIcon(XElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException("element");
            }

            var mimeType = element.GetDescendantValue(uPnpNamespaces.ud.GetName("mimetype"));
            var width = element.GetDescendantValue(uPnpNamespaces.ud.GetName("width"));
            var height = element.GetDescendantValue(uPnpNamespaces.ud.GetName("height"));
            var depth = element.GetDescendantValue(uPnpNamespaces.ud.GetName("depth"));
            var url = element.GetDescendantValue(uPnpNamespaces.ud.GetName("url"));

            var widthValue = int.Parse(width, NumberStyles.Any, UsCulture);
            var heightValue = int.Parse(height, NumberStyles.Any, UsCulture);

            return new DeviceIcon
            {
                Depth = depth,
                Height = heightValue,
                MimeType = mimeType,
                Url = url,
                Width = widthValue
            };
        }

        private static DeviceService Create(XElement element)
        {
            var type = element.GetDescendantValue(uPnpNamespaces.ud.GetName("serviceType"));
            var id = element.GetDescendantValue(uPnpNamespaces.ud.GetName("serviceId"));
            var scpdUrl = element.GetDescendantValue(uPnpNamespaces.ud.GetName("SCPDURL"));
            var controlURL = element.GetDescendantValue(uPnpNamespaces.ud.GetName("controlURL"));
            var eventSubURL = element.GetDescendantValue(uPnpNamespaces.ud.GetName("eventSubURL"));

            return new DeviceService
            {
                ControlUrl = controlURL,
                EventSubUrl = eventSubURL,
                ScpdUrl = scpdUrl,
                ServiceId = id,
                ServiceType = type
            };
        }

        #region Events

        public event EventHandler<TransportStateEventArgs> PlaybackChanged;
        public event EventHandler<CurrentIdEventArgs> CurrentIdChanged;

        private void NotifyPlaybackChanged(TRANSPORTSTATE state)
        {
            if (PlaybackChanged != null)
            {
                PlaybackChanged.Invoke(this, new TransportStateEventArgs
                {
                    State = state
                });
            }
        }

        private void NotifyCurrentIdChanged(string value)
        {
            if (CurrentIdChanged != null)
                CurrentIdChanged.Invoke(this, new CurrentIdEventArgs { Id = value });
        }

        #endregion

        #region IDisposable

        bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _timer.Dispose();
            }
        }

        #endregion

        public override string ToString()
        {
            return String.Format("{0} - {1}", Properties.Name, Properties.BaseUrl);
        }

    }

    public enum TRANSPORTSTATE
    {
        STOPPED,
        PLAYING,
        TRANSITIONING,
        PAUSED_PLAYBACK,
        PAUSED
    }
}
