using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Scene-level access point for deterministic stage random streams.
    ///
    /// Add one provider to a stage scene. Gameplay systems may then request
    /// independent streams such as EnemySpawns, Loot, Props, Weather, and
    /// Rewards without reading or changing NetworkRunState directly.
    /// </summary>
    public sealed class StageSeedProvider : MonoBehaviour
    {
        public StageSeedContext? CurrentContext
        {
            get;
            private set;
        }

        public bool IsReady => CurrentContext.HasValue;

        public event Action<StageSeedContext> ContextReady;

        private NetworkRunState boundRunState;
        private Coroutine bindRoutine;

        private void OnEnable()
        {
            bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }

            UnbindRunState();
            CurrentContext = null;
        }

        public int DeriveSeed(string systemName)
        {
            return RequireContext().DeriveSeed(systemName);
        }

        public System.Random CreateRandom(string systemName)
        {
            return RequireContext().CreateRandom(systemName);
        }

        public string BuildStreamName(string systemName)
        {
            return RequireContext().BuildStreamName(systemName);
        }

        public bool TryGetContext(out StageSeedContext context)
        {
            if (CurrentContext.HasValue)
            {
                context = CurrentContext.Value;
                return true;
            }

            context = default;
            return false;
        }

        private IEnumerator BindWhenReady()
        {
            while (
                NetworkRunState.Instance == null ||
                !NetworkRunState.Instance.IsSpawned
            )
            {
                yield return null;
            }

            BindRunState(NetworkRunState.Instance);
            bindRoutine = null;
        }

        private void BindRunState(NetworkRunState runState)
        {
            if (boundRunState == runState)
            {
                RefreshContext();
                return;
            }

            UnbindRunState();
            boundRunState = runState;

            boundRunState.MasterSeed.OnValueChanged += HandleSeedChanged;
            boundRunState.CurrentStage.OnValueChanged += HandleStageChanged;
            boundRunState.ConfigId.OnValueChanged += HandleConfigIdChanged;
            boundRunState.ConfigVersion.OnValueChanged +=
                HandleConfigVersionChanged;

            RefreshContext();
        }

        private void UnbindRunState()
        {
            if (boundRunState == null)
            {
                return;
            }

            boundRunState.MasterSeed.OnValueChanged -= HandleSeedChanged;
            boundRunState.CurrentStage.OnValueChanged -= HandleStageChanged;
            boundRunState.ConfigId.OnValueChanged -= HandleConfigIdChanged;
            boundRunState.ConfigVersion.OnValueChanged -=
                HandleConfigVersionChanged;

            boundRunState = null;
        }

        private void RefreshContext()
        {
            if (
                boundRunState == null ||
                !boundRunState.IsInitialized ||
                boundRunState.StageIndex < 1
            )
            {
                CurrentContext = null;
                return;
            }

            StageSeedContext context = new StageSeedContext(
                boundRunState.Seed,
                boundRunState.StageIndex,
                boundRunState.CurrentConfigId,
                boundRunState.ConfigVersion.Value
            );

            CurrentContext = context;
            ContextReady?.Invoke(context);

            Debug.Log(
                $"[Stage Seed] Context ready: {context}",
                this
            );
        }

        private StageSeedContext RequireContext()
        {
            if (!CurrentContext.HasValue)
            {
                throw new InvalidOperationException(
                    "StageSeedProvider was used before the synchronized " +
                    "run state and stage seed were ready."
                );
            }

            return CurrentContext.Value;
        }

        private void HandleSeedChanged(
            int previousValue,
            int currentValue
        )
        {
            RefreshContext();
        }

        private void HandleStageChanged(
            int previousValue,
            int currentValue
        )
        {
            RefreshContext();
        }

        private void HandleConfigIdChanged(
            FixedString64Bytes previousValue,
            FixedString64Bytes currentValue
        )
        {
            RefreshContext();
        }

        private void HandleConfigVersionChanged(
            int previousValue,
            int currentValue
        )
        {
            RefreshContext();
        }
    }
}
