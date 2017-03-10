using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFCachingProvider.Caching;
using Microsoft.ApplicationServer.Caching;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Web.Caching;
using System.Diagnostics;

namespace EFCachingProvider.Web
{
    public class AppFabricCache : ICache
    {
        private readonly string _cacheName;
        private DataCache _dataCache;
        private static DataCacheFactory cacheFactory;
        private object syncHandle = new object();
        private IList<string> _regions = new List<string>();
        private const string DependentEntitySetPrefix = "dependent_entity_set_";
        private List<string> _ignoredTables = new List<string>();

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="AppFabricCache"/> class with 
        /// AppFabric cache client configuration parameters coming from the 
        /// application configuration file&quot;s &lt;dataCacheClient&gt; section.
        /// </summary>
        /// <remarks>
        /// See <a href='http://msdn.microsoft.com/en-us/library/ee790823.aspx'>Get Started with a Windows Server AppFabric Cache Client (XML)</a> 
        /// and <a href='http://msdn.microsoft.com/en-us/library/ee790816.aspx'>Application Configuration Settings (Windows Server AppFabric Caching)</a>
        /// </remarks>
        public AppFabricCache(string cacheName) : this(cacheName, new DataCacheFactoryConfiguration()) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="AppFabricCache"/> class.
        /// </summary>
        /// <param name="cacheName">Name of the cache.</param>
        /// <param name="configuration">The configuration.</param>
        /// <remarks>
        /// This overloaded constructor enables you to programmatically configure a cache client.
        /// First create an instance of the DataCacheFactoryConfiguration class and use its properties
        /// to define the settings for the cache client. Then pass this object to this constructor.
        /// </remarks>
        public AppFabricCache(string cacheName, DataCacheFactoryConfiguration configuration)
        {
            if (String.IsNullOrEmpty(cacheName))
                throw new ArgumentException("cacheName is null or empty.", "cacheName");
            if (configuration == null)
                throw new ArgumentNullException("configuration", "configuration is null.");

            _cacheName = cacheName;

            _ignoredTables.AddRange(new string[] { "Picture", "Setting", "LocaleStringResource", "ScheduleTask", "ProductVariant", "Product", "Discount" });

            if (cacheFactory == null)
            {
                lock (syncHandle)
                {
                    if (cacheFactory == null)
                        cacheFactory = new DataCacheFactory(configuration);
                }
            }

            _dataCache = cacheFactory.GetCache(cacheName);
        }
        #endregion

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
            value = this._dataCache.Get(key);

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
            //setting, localestringresource etc are cached in caching service. it is more efficient that way

            /*ignoredTables.ForEach(ignoredTable =>
                {
                    if (dependentEntitySets.Contains(ignoredTable, StringComparer.InvariantCultureIgnoreCase))
                    {
                        exitFunction = true;
                        return true;
                    }
                });*/

            bool exitFunction = _ignoredTables.Any(ignoredTable => dependentEntitySets.Contains(ignoredTable, StringComparer.InvariantCultureIgnoreCase));

            if (exitFunction || (value as dynamic).Rows.Count == 0)
                return;
            
            //Debug.WriteLine(key);
            key = GetCacheKey(key);
            var cache = this._dataCache;

            foreach (var entitySet in dependentEntitySets)
            {
                //Debug.WriteLine(entitySet);
                this.EnsureEntryExists(DependentEntitySetPrefix + entitySet, key);
            }

            try
            {
                /*var policy = new CacheItemPolicy();
                policy.AbsoluteExpiration = absoluteExpiration;*/
                TimeSpan timeSpan = new TimeSpan(24, 0, 0);
                _dataCache.Add(key, value, timeSpan);                

                /*if (slidingExpiration != null && slidingExpiration.Minutes > 1)
                    _dataCache.Add(key, value, slidingExpiration);
                else
                {
                    
                }*/
                //ChangeMonitor cm = new ChangeMonitor(
                //CacheDependency cd = new CacheDependency(new string[0], dependentEntitySets.Select(c => DependentEntitySetPrefix + c).ToArray());
                
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
            bool exitFunction = _ignoredTables.Any(ignoredTable => entitySets.Contains(ignoredTable, StringComparer.InvariantCultureIgnoreCase));

            if (exitFunction)
                return;

            foreach (string entitySet in entitySets)
            {
                InvalidateSet(entitySet);
                this._dataCache.Remove(DependentEntitySetPrefix + entitySet);
            }
        }

        /// <summary>
        /// Invalidates all cache entries which are dependent on the specified entity set.
        /// </summary>
        /// <param name="entitySets">The entity set.</param>
        public void InvalidateSet(string entitySet)
        {
            string key = DependentEntitySetPrefix + entitySet;
            object item = _dataCache.Get(key);

            if (item != null)
            {
                if (item is List<object>)
                {
                    var items = item as List<object>;

                    foreach (var i in items)
                    {
                        _dataCache.Remove(i as string);
                    }
                }
                else
                {
                    _dataCache.Remove(item as string);
                }
            }           
        }

        /// <summary>
        /// Invalidates cache entry with a given key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        public void InvalidateItem(string key)
        {
            key = GetCacheKey(key);
            this._dataCache.Remove(key);
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

        private void EnsureEntryExists(string dsKey, object value)
        {
            var cache = this._dataCache;
            object existingVal = cache.Get(dsKey);

            if (existingVal == null)
            {
                try
                {
                    /*DateTime dateTime = DateTime.Now.AddDays(1);
                    dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Local);
                    var policy = new CacheItemPolicy();                    
                    policy.AbsoluteExpiration = dateTime;*/
                    TimeSpan timeSpan = new TimeSpan(24, 0, 0);
                    cache.Add(dsKey, value, timeSpan);
                }
                catch (Exception)
                {
                    // log exception.
                }
            }
            else
            {
                if (!(existingVal is List<object>))
                {
                    var vals = new List<object>(new object[] { existingVal, value });
                    cache.Put(dsKey, vals);
                }
                else
                {
                    (existingVal as List<object>).Add(value);
                    cache.Put(dsKey, existingVal);
                }
            }
        }
    }
}
