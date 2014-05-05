using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Providers
{
    public class ItemIdentifier<TLookupInfo, TIdentity>
        where TLookupInfo : ItemLookupInfo
        where TIdentity : IItemIdentity
    {
        public async Task<IEnumerable<TIdentity>> FindIdentities(TLookupInfo item, IProviderManager providerManager, CancellationToken cancellationToken)
        {
            IEnumerable<IItemIdentityProvider<TLookupInfo, TIdentity>> providers = providerManager.GetItemIdentityProviders<TLookupInfo, TIdentity>();
            IEnumerable<IItemIdentityConverter<TIdentity>> converters = providerManager.GetItemIdentityConverters<TIdentity>();

            var identities = new List<IdentityPair>();

            foreach (var provider in providers)
            {
                var result = new IdentityPair
                {
                    Identity = await provider.FindIdentity(item),
                    Order = provider.Order
                };

                if (!Equals(result.Identity, default(TIdentity)))
                {
                    identities.Add(result);
                }
            }

            var convertersAvailable = new List<IItemIdentityConverter<TIdentity>>(converters);
            bool changesMade;

            do
            {
                changesMade = false;

                for (int i = convertersAvailable.Count - 1; i >= 0; i--)
                {
                    IItemIdentityConverter<TIdentity> converter = convertersAvailable[i];
                    IdentityPair input = identities.FirstOrDefault(id => id.Identity.Type == converter.SourceType);
                    IEnumerable<IdentityPair> existing = identities.Where(id => id.Identity.Type == converter.ResultType);

                    if (input != null && !existing.Any(id => id.Order <= converter.Order))
                    {
                        var result = new IdentityPair
                        {
                            Identity = await converter.Convert(input.Identity).ConfigureAwait(false),
                            Order = converter.Order
                        };

                        if (!Equals(result.Identity, default(TIdentity)))
                        {
                            identities.Add(result);
                            convertersAvailable.RemoveAt(i);
                            changesMade = true;
                        }
                    }
                }
            } while (changesMade);

            return identities.Select(id => id.Identity);
        }

        private class IdentityPair
        {
            public TIdentity Identity;
            public int Order;
        }
    }
}