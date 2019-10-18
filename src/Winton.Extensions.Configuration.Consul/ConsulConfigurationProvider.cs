// Copyright (c) Winton. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENCE in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Winton.Extensions.Configuration.Consul.Extensions;

namespace Winton.Extensions.Configuration.Consul
{
    internal sealed class ConsulConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private readonly IConsulConfigurationClient _consulConfigClient;
        private readonly IConsulConfigurationSource _source;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _pollTask;

        public ConsulConfigurationProvider(
            IConsulConfigurationSource source,
            IConsulConfigurationClient consulConfigClient)
        {
            if (source.Parser == null)
            {
                throw new ArgumentNullException(nameof(source.Parser));
            }

            _consulConfigClient = consulConfigClient;
            _source = source;
            _cancellationTokenSource = new CancellationTokenSource();

            if (source.ReloadOnChange)
            {
                var token = _cancellationTokenSource.Token;
                _pollTask = Task.Run(
                    async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await _consulConfigClient.PollForChanges(_source.Key, _source.OnWatchException, token);
                            await DoLoad(true).ConfigureAwait(false);
                            OnReload();
                        }
                    }, token);
            }
        }

        public override void Load()
        {
            DoLoad(false).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _pollTask.Wait(500);
            _cancellationTokenSource.Dispose();
        }

        private async Task DoLoad(bool reloading)
        {
            try
            {
                QueryResult<KVPair[]> result = await _consulConfigClient
                    .GetConfig(_source.Key, _cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                if (!result.HasValue() && !_source.Optional)
                {
                    if (!reloading)
                    {
                        throw new Exception(
                            $"The configuration for key {_source.Key} was not found and is not optional.");
                    }

                    // Don't overwrite mandatory config with empty data if not found when reloading
                    return;
                }

                string keyToRemove = _source.KeyToRemove ?? _source.Key;

                Data = (result?.Response ?? new KVPair[0])
                    .Where(kvp => kvp.HasValue())
                    .SelectMany(kvp => kvp.ConvertToConfig(keyToRemove, _source.Parser))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                var exceptionContext = new ConsulLoadExceptionContext(_source, exception);
                _source.OnLoadException?.Invoke(exceptionContext);
                if (!exceptionContext.Ignore)
                {
                    throw;
                }
            }
        }
    }
}