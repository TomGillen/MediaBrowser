﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Localization;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace MediaBrowser.Providers.MediaInfo
{
    public class FFProbeProvider : ICustomMetadataProvider<Episode>,
        ICustomMetadataProvider<MusicVideo>,
        ICustomMetadataProvider<Movie>,
        ICustomMetadataProvider<AdultVideo>,
        ICustomMetadataProvider<LiveTvVideoRecording>,
        ICustomMetadataProvider<LiveTvAudioRecording>,
        ICustomMetadataProvider<Trailer>,
        ICustomMetadataProvider<Video>,
        ICustomMetadataProvider<Audio>,
        IHasChangeMonitor,
        IHasOrder,
        IForcedProvider
    {
        private readonly ILogger _logger;
        private readonly IIsoManager _isoManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IItemRepository _itemRepo;
        private readonly IBlurayExaminer _blurayExaminer;
        private readonly ILocalizationManager _localization;
        private readonly IApplicationPaths _appPaths;
        private readonly IJsonSerializer _json;
        private readonly IEncodingManager _encodingManager;
        private readonly IFileSystem _fileSystem;

        public string Name
        {
            get { return "ffprobe"; }
        }

        public Task<ItemUpdateType> FetchAsync(Episode item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(MusicVideo item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(Movie item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(AdultVideo item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(LiveTvVideoRecording item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(Trailer item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(Video item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchVideoInfo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(Audio item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchAudioInfo(item, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(LiveTvAudioRecording item, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return FetchAudioInfo(item, cancellationToken);
        }

        public FFProbeProvider(ILogger logger, IIsoManager isoManager, IMediaEncoder mediaEncoder, IItemRepository itemRepo, IBlurayExaminer blurayExaminer, ILocalizationManager localization, IApplicationPaths appPaths, IJsonSerializer json, IEncodingManager encodingManager, IFileSystem fileSystem)
        {
            _logger = logger;
            _isoManager = isoManager;
            _mediaEncoder = mediaEncoder;
            _itemRepo = itemRepo;
            _blurayExaminer = blurayExaminer;
            _localization = localization;
            _appPaths = appPaths;
            _json = json;
            _encodingManager = encodingManager;
            _fileSystem = fileSystem;
        }

        private readonly Task<ItemUpdateType> _cachedTask = Task.FromResult(ItemUpdateType.None);
        public Task<ItemUpdateType> FetchVideoInfo<T>(T item, IDirectoryService directoryService, CancellationToken cancellationToken)
            where T : Video
        {
            if (item.LocationType != LocationType.FileSystem)
            {
                return _cachedTask;
            }

            if (item.VideoType == VideoType.Iso && !_isoManager.CanMount(item.Path))
            {
                return _cachedTask;
            }

            if (item.VideoType == VideoType.HdDvd)
            {
                return _cachedTask;
            }

            if (item.IsPlaceHolder)
            {
                return _cachedTask;
            }

            var prober = new FFProbeVideoInfo(_logger, _isoManager, _mediaEncoder, _itemRepo, _blurayExaminer, _localization, _appPaths, _json, _encodingManager, _fileSystem);

            return prober.ProbeVideo(item, directoryService, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAudioInfo<T>(T item, CancellationToken cancellationToken)
            where T : Audio
        {
            if (item.LocationType != LocationType.FileSystem)
            {
                return _cachedTask;
            }

            var prober = new FFProbeAudioInfo(_mediaEncoder, _itemRepo, _appPaths, _json);

            return prober.Probe(item, cancellationToken);
        }

        public bool HasChanged(IHasMetadata item, IDirectoryService directoryService, DateTime date)
        {
            if (item.DateModified > date)
            {
                return true;
            }

            if (item.SupportsLocalMetadata)
            {
                var video = item as Video;

                if (video != null && !video.IsPlaceHolder)
                {
                    var prober = new FFProbeVideoInfo(_logger, _isoManager, _mediaEncoder, _itemRepo, _blurayExaminer, _localization, _appPaths, _json, _encodingManager, _fileSystem);

                    return !video.SubtitleFiles.SequenceEqual(prober.GetSubtitleFiles(video, directoryService).Select(i => i.FullName).OrderBy(i => i), StringComparer.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        public int Order
        {
            get
            {
                // Run last
                return 100;
            }
        }
    }
}
