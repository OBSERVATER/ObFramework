using System;
using System.Collections.Generic;

namespace ObFramework.Core.Service
{
    internal interface IQueryService : IService
    {
        IRuntimeDataGroup<TResult> SendQuery<TQuery, TResult>(bool refresh = false) where TQuery : class, IQuery<TResult>, new() where TResult : class, IRuntimeData;
    }
    public class QueryService : IQueryService
    {
        private readonly Dictionary<Type, object> _queries;
        private readonly IArchitecture _architecture;

        public QueryService(IArchitecture architecture)
        {
            _queries = new Dictionary<Type, object>();
            _architecture = architecture;
        }

        public IRuntimeDataGroup<TResult> SendQuery<TQuery, TResult>(bool refresh = false)
            where TQuery : class, IQuery<TResult>, new()
            where TResult : class, IRuntimeData
        {
            if (!_queries.TryGetValue(typeof(TQuery), out var instance))
            {
                var query = new TQuery();
                query.SetArchitecture(_architecture);
                _queries.Add(typeof(TQuery), query);
                instance = query;
            }

            return ((TQuery)instance).Q(refresh);
        }
    }
}
