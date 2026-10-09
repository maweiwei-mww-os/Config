using System;
using System.Threading;

namespace GameJson
{
    public enum ConfigVersionPolicy { AllowAny, Increasing }
    public enum ConfigFailureCode { None, PreparationFailed, VersionRejected, StaleCandidate, WrongStore, AlreadyCommitted, Discarded }
    public readonly struct ConfigFailure
    {
        public ConfigFailureCode Code { get; }
        public string Message { get; }
        internal ConfigFailure(ConfigFailureCode code, string message) { Code = code; Message = message; }
    }
    public sealed class ConfigSnapshot<T> where T : class
    {
        public T Value { get; }
        public int Version { get; }
        public long Generation { get; }
        internal ConfigSnapshot(T value, int version, long generation) { Value = value; Version = version; Generation = generation; }
    }
    public sealed class PreparedConfig<T> : IDisposable where T : class
    {
        internal readonly ConfigStore<T> Owner;
        internal bool Committed;
        internal bool Discarded;
        internal T Value;
        public int Version { get; }
        public long BaseGeneration { get; }
        internal PreparedConfig(ConfigStore<T> owner, T value, int version, long generation)
        { Owner = owner; Value = value; Version = version; BaseGeneration = generation; }
        public void Dispose() => Owner.Discard(this);
    }
    public sealed class ConfigStore<T> where T : class
    {
        readonly Func<IConfigData, T> bind;
        readonly ConfigVersionPolicy policy;
        readonly object commitGate = new object();
        ConfigSnapshot<T> current;
        public T Current => Capture()?.Value;
        // AllowAny preserves 0.1 behavior. Production update pipelines should select Increasing.
        public ConfigStore(Func<IConfigData, T> bind, ConfigVersionPolicy policy = ConfigVersionPolicy.AllowAny)
        {
            this.bind = bind ?? throw new ArgumentNullException(nameof(bind));
            if (policy != ConfigVersionPolicy.AllowAny && policy != ConfigVersionPolicy.Increasing) throw new ArgumentOutOfRangeException(nameof(policy));
            this.policy = policy;
        }
        public ConfigSnapshot<T> Capture() => Volatile.Read(ref current);
        public bool TryPrepare(Func<IConfigData> load, out PreparedConfig<T> candidate, out ConfigFailure error)
        {
            candidate = null;
            var baseline = Capture();
            try {
                if (load == null) throw new ArgumentNullException(nameof(load));
                var data = load() ?? throw new ArgumentException("Configuration loader returned null.");
                if (data.Version < 1) throw new ArgumentException("Configuration version must be positive.");
                if (policy == ConfigVersionPolicy.Increasing && baseline != null && data.Version <= baseline.Version)
                    return Fail(ConfigFailureCode.VersionRejected, "Candidate version must be newer than " + baseline.Version + ".", out error);
                var value = bind(data) ?? throw new ArgumentException("Configuration binder returned null.");
                candidate = new PreparedConfig<T>(this, value, data.Version, baseline?.Generation ?? 0);
                error = default; return true;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            { return Fail(ConfigFailureCode.PreparationFailed, e.Message, out error); }
        }
        public bool TryCommit(PreparedConfig<T> candidate, out ConfigFailure error)
        {
            if (candidate == null || !ReferenceEquals(candidate.Owner, this))
                return Fail(ConfigFailureCode.WrongStore, "Candidate belongs to a different store or is null.", out error);
            lock (commitGate) {
                if (candidate.Committed) return Fail(ConfigFailureCode.AlreadyCommitted, "Candidate has already been committed.", out error);
                if (candidate.Discarded) return Fail(ConfigFailureCode.Discarded, "Candidate has been discarded.", out error);
                var baseline = current;
                if ((baseline?.Generation ?? 0) != candidate.BaseGeneration)
                    return Fail(ConfigFailureCode.StaleCandidate, "The active snapshot changed after preparation. Prepare again.", out error);
                var next = new ConfigSnapshot<T>(candidate.Value, candidate.Version, checked(candidate.BaseGeneration + 1));
                candidate.Committed = true;
                candidate.Value = null;
                Volatile.Write(ref current, next);
                error = default; return true;
            }
        }
        internal void Discard(PreparedConfig<T> candidate)
        {
            lock (commitGate) {
                if (candidate.Committed || candidate.Discarded) return;
                candidate.Discarded = true;
                candidate.Value = null;
            }
        }
        public bool TryPublish(IConfigData data, out string error)
        {
            bool success = TryPrepare(() => data, out var candidate, out var failure) && TryCommit(candidate, out failure);
            error = success ? null : failure.Message; return success;
        }
        static bool Fail(ConfigFailureCode code, string message, out ConfigFailure error)
        { error = new ConfigFailure(code, message); return false; }
    }
}
