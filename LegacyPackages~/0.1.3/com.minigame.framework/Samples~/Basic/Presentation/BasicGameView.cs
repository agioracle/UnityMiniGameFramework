using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniGame.Framework;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.Examples.Basic
{
    public sealed class BasicGameView : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button[] cells;
        [SerializeField] private TMP_Text heading;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Text movesLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text targetLabel;
        [SerializeField] private TMP_Text startLabel;
        [SerializeField] private TMP_Text pauseLabel;
        [SerializeField, Min(1)] private int moves = 8;
        [SerializeField, Min(1)] private int goal = 5;
        [SerializeField] private int seed = 31;
        [SerializeField] private BasicLevelConfig levelConfig;
        private FrameworkRuntime framework;
        private LevelRules rules;
        private ReactiveProperty<LevelSnapshot> display;
        private IDisposable renderSubscription;
        private IDisposable userPause;
        private CancellationTokenSource lifetime;
        private bool initialized;
        private bool visible = true;
        private float pulse;
        private UnityEngine.Events.UnityAction[] cellHandlers;
        private bool listenersAttached;
        private BasicProgressStore progress;

        public void UseProgressDirectory(string directory)
        {
            if (rules != null && rules.Snapshot.Status == LevelStatus.Playing)
                throw new InvalidOperationException("Change progress storage outside a round.");
            progress = new BasicProgressStore(directory);
        }

        public bool IsReady => initialized;
        public LevelSnapshot Current => rules != null ? rules.Snapshot : default;
        public Button StartButton => startButton;
        public Button Cell(int index) => cells[index];
        public bool IsPaused => userPause != null;

        private void Awake()
        {
            lifetime = new CancellationTokenSource();
            if (!ValidateBindings())
            {
                Debug.LogError("BasicGameView has missing serialized bindings.", this);
                enabled = false;
                return;
            }
            startButton.interactable = false;
            pauseButton.interactable = false;
            BasicLevelConfig.Snapshot settings = levelConfig ? levelConfig.ReadSnapshot() : new BasicLevelConfig.Snapshot(moves, goal, seed);
            rules = new LevelRules(cells.Length, settings.Moves, settings.Goal, settings.Seed);
            display = new ReactiveProperty<LevelSnapshot>(rules.Snapshot);
            cellHandlers = new UnityEngine.Events.UnityAction[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                int index = i;
                cellHandlers[i] = () => Select(index);
            }
            framework = new FrameworkRuntime();
            InitializeAsync(lifetime.Token).Forget();
        }

        private async UniTask InitializeAsync(CancellationToken token)
        {
            try
            {
                await framework.InitializeAsync(new FrameworkOptions(), token);
                token.ThrowIfCancellationRequested();
                if (progress == null) UseProgressDirectory(System.IO.Path.Combine(Application.persistentDataPath, "MiniGameBasic"));
                initialized = true;
                startButton.interactable = true;
                Render(rules.Snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                if (this && status) status.text = "STARTUP FAILED  /  See Console";
            }
        }

        private void OnEnable()
        {
            if (display == null) return;
            startButton.onClick.AddListener(StartRound);
            pauseButton.onClick.AddListener(TogglePause);
            for (int i = 0; i < cells.Length; i++) cells[i].onClick.AddListener(cellHandlers[i]);
            listenersAttached = true;
            renderSubscription = display.Subscribe(Render);
        }

        private void OnDisable()
        {
            if (listenersAttached)
            {
                if (startButton) startButton.onClick.RemoveListener(StartRound);
                if (pauseButton) pauseButton.onClick.RemoveListener(TogglePause);
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i]) cells[i].onClick.RemoveListener(cellHandlers[i]);
                listenersAttached = false;
            }
            renderSubscription?.Dispose();
            renderSubscription = null;
            userPause?.Dispose();
            userPause = null;
        }

        private void StartRound()
        {
            if (!initialized || !isActiveAndEnabled) return;
            userPause?.Dispose();
            userPause = null;
            rules.Start();
            display.Value = rules.Snapshot;
        }

        private void Select(int index)
        {
            if (!initialized || !isActiveAndEnabled || !visible || userPause != null) return;
            if (!rules.Select(index)) return;
            if (rules.Snapshot.Status == LevelStatus.Won || rules.Snapshot.Status == LevelStatus.Lost)
                progress.Record(rules.Snapshot.Status == LevelStatus.Won);
            display.Value = rules.Snapshot;
        }

        public void TogglePause()
        {
            if (!initialized || rules.Snapshot.Status != LevelStatus.Playing) return;
            if (userPause == null) userPause = framework.Pauses.Acquire();
            else { userPause.Dispose(); userPause = null; }
            Render(rules.Snapshot);
        }

        private void Render(LevelSnapshot state)
        {
            if (!this || !isActiveAndEnabled) return;
            bool playing = state.Status == LevelStatus.Playing;
            heading.text = state.Status == LevelStatus.Won ? "Beautifully done." : state.Status == LevelStatus.Lost ? "One more try?" : "Find your focus.";
            movesLabel.text = state.Moves.ToString("00");
            scoreLabel.text = state.Matches + " / " + state.Goal;
            targetLabel.text = playing ? (state.Target + 1).ToString("00") : "—";
            startLabel.text = state.Status == LevelStatus.Ready ? "LET'S PLAY" : "PLAY AGAIN";
            pauseLabel.text = userPause == null ? "PAUSE" : "RESUME";
            pauseButton.interactable = initialized && playing;
            status.text = userPause != null ? "Take your time. Tap RESUME when ready." :
                state.Status == LevelStatus.Won ? "All targets found. A fresh round awaits." :
                state.Status == LevelStatus.Lost ? "No moves left. Reset and try a new rhythm." :
                playing ? "Tap the highlighted number. Every tap uses one move." : "Find " + goal + " targets in " + moves + " moves.";
            if (!playing && progress != null)
                status.text += progress.Status == StoreStatus.Success || progress.Status == StoreStatus.Missing
                    ? "  Saved wins: " + progress.Value.wonRounds
                    : "  Progress unavailable: " + progress.Status;
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].interactable = initialized && playing && userPause == null;
                bool target = playing && i == state.Target;
                cells[i].image.color = target ? new Color32(255, 180, 84, 255) : new Color32(255, 255, 255, 255);
                cells[i].transform.localScale = Vector3.one;
            }
            pulse = 0;
        }

        private void Update()
        {
            if (!initialized) return;
            if (visible) framework.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            if (rules.Snapshot.Status != LevelStatus.Playing || userPause != null || !visible) return;
            pulse += Time.unscaledDeltaTime;
            cells[rules.Snapshot.Target].transform.localScale = Vector3.one * (1 + Mathf.Sin(pulse * 3) * 0.025f);
        }

        private void OnApplicationPause(bool paused) => visible = !paused;

        private void OnDestroy()
        {
            lifetime?.Cancel();
            renderSubscription?.Dispose();
            userPause?.Dispose();
            display?.Dispose();
            framework?.Dispose();
            lifetime?.Dispose();
        }

        private bool ValidateBindings()
        {
            if (!startButton || !pauseButton || !heading || !status || !movesLabel || !scoreLabel || !targetLabel ||
                !startLabel || !pauseLabel || cells == null || cells.Length < 2 || moves < 1 || goal < 1 || goal > moves) return false;
            foreach (Button button in cells) if (!button) return false;
            if (levelConfig)
            {
                try { levelConfig.ReadSnapshot(); }
                catch (ArgumentException) { return false; }
            }
            return true;
        }
    }
}
