using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFCachingProvider.Caching;
using System.Runtime.Caching;
using System.Web.Caching;
using System.Security.Cryptography;

namespace EFCachingProvider.Web
{
    public class RuntimeCache : ICache
    {
        private const string DependentEntitySetPrefix = "dependent_entity_set_";
        private MemoryCache _cache = MemoryCache.Default;

        /// <summary>
        /// Tries to the get entry by key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The retrieved value.</param>
        /// <returns>
        /// A value of <c>true</c> if entry was found in the cache, <c>false</c> otherwise.
        /// </returns>
        public bool GetItem(string key, out object value)
        {
            key = GetCacheKey(key);
            value = this._cache.Get(key);

            return value != null;
        }

        /// <summary>
        /// Adds the specified entry to the cache.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The entry value.</param>
        /// <param name="dependentEntitySets">The list of dependent entity sets.</param>
        /// <param name="slidingExpiration">The sliding expiration.</param>
        /// <param name="absoluteExpiration">The absolute expiration.</param>
        public void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration, DateTime absoluteExpiration)
        {
            key = GetCacheKey(key);
            var cache = this._cache;

            foreach (var entitySet in dependentEntitySets)
            {
                this.EnsureEntryExists(DependentEntitySetPrefix + entitySet);
            }

            try
            {
                var policy = new CacheItemPolicy();
                if (slidingExpiration != null)
                    policy.SlidingExpiration = slidingExpiration;
                else
                    policy.AbsoluteExpiration = absoluteExpiration;
                //ChangeMonitor cm = new ChangeMonitor(
                //CacheDependency cd = new CacheDependency(new string[0], dependentEntitySets.Select(c => DependentEntitySetPrefix + c).ToArray());
                cache.Add(key, value, policy);                
            }
            catch (Exception)
            {
                // there's a possibility that one of the dependencies has been evicted by another thread
                // in this case just don't put this item in the cache
            }        
        }

        /// <summary>
        /// Invalidates all cache entries which are dependent on any of the specified entity sets.
        /// </summary>
        /// <param name="entitySets">The entity sets.</param>
        public void InvalidateSets(IEnumerable<string> entitySets)
        {
            foreach (string entitySet in entitySets)
            {
                this._cache.Remove(DependentEntitySetPrefix + entitySet);
            }
        }

        /// <summary>
        /// Invalidates all cache entries which are dependent on the specified entity set.
        /// </summary>
        /// <param name="entitySets">The entity set.</param>
        public void InvalidateSet(string entitySet)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Invalidates cache entry with a given key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        public void InvalidateItem(string key)
        {
            key = GetCacheKey(key);
            this._cache.Remove(key);
        }

        /// <summary>
        /// Hashes the query to produce cache key..
        /// </summary>
        /// <param name="query">The query.</param>
        /// <returns>Hashed query which becomes a cache key.</returns>
        private static string GetCacheKey(string query)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(query);
            string hashString = Convert.ToBase64String(MD5.Create().ComputeHash(bytes));
            return hashString;
        }

        private void EnsureEntryExists(string key)
        {
            var cache = this._cache;

            if (cache.Get(key) == null)
            {
                try
                {
                    cache.Add(key, key, Cache.NoAbsoluteExpiration);
                }
                catch (Exception)
                {
                    // ignore exceptions.
                }
            }
        }
    }
}
