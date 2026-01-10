// Global state
let selectedQuests = new Set();
let allQuests = { available: [], accepted: [], completed: [], history: [] };
let currentUser = null;

// History tab state (lazy loading for all completed quests including claimed)
let historyQuestsState = {
    loaded: false,
    page: 0,
    pageSize: 5,
    hasMore: true,
    loading: false
};

// Quest running state
let questRunningState = {
    isRunning: false,
    runningQuests: [],
    currentQuestId: null,
    statusInterval: null,
    progressInterval: null,
    isFocused: true
};

// Fun status messages that rotate every 8 seconds
const funStatusMessages = [
    "🎮 Đang cày game như một pro player...",
    "🎬 Đang enjoy video cực chill...",
    "🚀 Quest đang được xử lý, ngồi uống trà đi!",
    "⚡ Đang farm progress siêu tốc...",
    "🎯 Focus mode: ON - Target: Complete!",
    "🔥 Hot streak! Quest đang chạy mượt mà...",
    "🌟 Grinding quests như một boss...",
    "☕ Relax đi, để tôi handle cho...",
    "🎪 Show time! Quest đang được thực hiện...",
    "💪 Power farming mode activated!",
    "🎲 Rolling for progress... Critical hit!",
    "🏆 Đang trên đường đến vinh quang...",
    "🎸 Quest vibes: Immaculate ✨",
    "🌈 Making magic happen...",
    "🎭 Playing the game, winning the prize!"
];

// Toast helper function
function showToast(message, type = 'info') {
    const backgrounds = {
        success: 'linear-gradient(135deg, #3ba55d 0%, #2d7d46 100%)',
        error: 'linear-gradient(135deg, #ed4245 0%, #c53639 100%)',
        warning: 'linear-gradient(135deg, #faa61a 0%, #d18617 100%)',
        info: 'linear-gradient(135deg, #5865f2 0%, #4752c4 100%)'
    };

    Toastify({
        text: message,
        duration: 3000,
        gravity: "bottom",
        position: "right",
        stopOnFocus: true,
        close: true,
        onClick: function () { },
        style: {
            background: backgrounds[type] || backgrounds.info,
            borderRadius: '8px',
            padding: '14px 18px',
            fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif",
            boxShadow: '0 8px 24px rgba(0, 0, 0, 0.4)',
            marginBottom: '12px'
        }
    }).showToast();
}

// Loading overlay
function showLoading(message = 'Loading...') {
    const overlay = document.getElementById('loadingOverlay');
    const text = document.getElementById('loadingText');
    if (overlay && text) {
        text.textContent = message;
        overlay.classList.remove('hidden');
    }
}

function hideLoading() {
    const overlay = document.getElementById('loadingOverlay');
    if (overlay) {
        overlay.classList.add('hidden');
    }
}

// Button state management
function setButtonState(button, state, text) {
    if (!button) return;

    button.disabled = state === 'loading';

    if (state === 'loading') {
        button.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${text}`;
    } else {
        const icon = button.dataset.icon || 'fab fa-discord';
        button.innerHTML = `<i class="${icon}"></i> ${text}`;
    }
}

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupEventListeners();
    setupCountdownClick();
});

function setupEventListeners() {
    // Login
    const btnLogin = document.getElementById('btnLogin');
    if (btnLogin) {
        btnLogin.dataset.icon = 'fab fa-discord';
        btnLogin.addEventListener('click', () => login());
    }

    // Tab switching
    document.querySelectorAll('.tab').forEach(tab => {
        tab.addEventListener('click', () => switchTab(tab.dataset.tab));
    });

    // Buttons
    document.getElementById('btnSettings')?.addEventListener('click', () => showSettings());
    document.getElementById('btnReload')?.addEventListener('click', () => {
        loadQuests(true);
        resetRefreshCountdown();
    });
    document.getElementById('btnStartSelected')?.addEventListener('click', () => startSelectedQuests());
    document.getElementById('btnStartAll')?.addEventListener('click', () => startAllAcceptedQuests());
    document.getElementById('btnStopAll')?.addEventListener('click', () => stopAllQuests());
    document.getElementById('btnCloseSettings')?.addEventListener('click', () => hideSettings());
    document.getElementById('btnSaveSettings')?.addEventListener('click', () => saveSettings());
    document.getElementById('btnLogout')?.addEventListener('click', () => logout());

    // Modal overlay
    document.querySelector('.modal-overlay')?.addEventListener('click', () => hideSettings());
    
    // Focus/Blur detection for progress updates
    setupFocusDetection();
}

// Focus detection - NO polling needed since backend sends progress via heartbeat
function setupFocusDetection() {
    document.addEventListener('visibilitychange', () => {
        questRunningState.isFocused = !document.hidden;
    });
    
    window.addEventListener('focus', () => {
        questRunningState.isFocused = true;
    });
    
    window.addEventListener('blur', () => {
        questRunningState.isFocused = false;
    });
}

function login() {
    const btnLogin = document.getElementById('btnLogin');
    setButtonState(btnLogin, 'loading', 'Opening Discord...');

    window.chrome.webview.postMessage({ action: 'login' });
    updateStatus('Opening Discord login window...');
}

function showLoginScreen() {
    document.getElementById('loginScreen').classList.remove('hidden');
    document.getElementById('mainApp').classList.add('hidden');

    const btnLogin = document.getElementById('btnLogin');
    setButtonState(btnLogin, 'normal', 'Login with Discord');
}

function hideLoginScreen() {
    document.getElementById('loginScreen').classList.add('hidden');
    document.getElementById('mainApp').classList.remove('hidden');
}

function loginSuccess(user) {
    currentUser = user;
    hideLoading();
    hideLoginScreen();

    // Update user profile
    const username = `${user.username}#${user.discriminator}`;
    document.getElementById('username').textContent = username;

    // Set avatar
    const avatarEl = document.getElementById('userAvatar');
    if (user.avatar) {
        avatarEl.src = `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png?size=128`;
    } else {
        avatarEl.src = `https://cdn.discordapp.com/embed/avatars/${parseInt(user.discriminator) % 5}.png`;
    }

    showToast(`Welcome back, ${user.username}!`, 'success');
    updateStatus('Ready');

    // Auto-load quests
    loadQuests();
}

function switchTab(tabName) {
    // Update tab buttons
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelector(`[data-tab="${tabName}"]`)?.classList.add('active');

    // Update tab content
    document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
    document.getElementById(`${tabName}QuestsTab`)?.classList.add('active');

    // Lazy load history quests on first access
    if (tabName === 'history' && !historyQuestsState.loaded) {
        loadHistoryQuestsPage(0);
        setupHistoryScrollListener();
    }
}

function setupHistoryScrollListener() {
    const historyTab = document.getElementById('historyQuestsTab');
    if (!historyTab || historyTab._scrollListenerAdded) return;
    
    historyTab._scrollListenerAdded = true;
    
    historyTab.addEventListener('scroll', () => {
        const { scrollTop, scrollHeight, clientHeight } = historyTab;
        
        console.log('History Scroll:', { scrollTop, scrollHeight, clientHeight, hasMore: historyQuestsState.hasMore, loading: historyQuestsState.loading });

        // Load more when scrolled to bottom (with 150px threshold)
        if (scrollTop + clientHeight >= scrollHeight - 150) {
            if (historyQuestsState.hasMore && !historyQuestsState.loading) {
                console.log('Loading more history quests...');
                loadHistoryQuestsPage(historyQuestsState.page + 1);
            }
        }
    });
    
    console.log('History scroll listener attached');
}

function loadHistoryQuestsPage(page) {
    if (historyQuestsState.loading) return;

    historyQuestsState.loading = true;
    historyQuestsState.page = page;

    const loadingMore = document.getElementById('historyLoadingMore');
    if (loadingMore) loadingMore.classList.remove('hidden');

    if (page === 0) {
        showLoading('Loading quest history...');
    }

    window.chrome.webview.postMessage({
        action: 'loadHistoryQuests',
        page: page,
        pageSize: historyQuestsState.pageSize
    });
}

function loadQuests(showLoadingOverlay = true) {
    if (showLoadingOverlay) {
        showLoading('Loading quests...');
    }
    updateStatus('Loading quests...');
    window.chrome.webview.postMessage({ action: 'loadQuests' });
}

// Countdown timer state
let countdownState = {
    interval: null,
    seconds: 0,
    totalSeconds: 0
};

// Start countdown timer for auto-refresh (integrated into Reload button)
function startRefreshCountdown(intervalMinutes = 5) {
    // Clear existing interval
    stopRefreshCountdown();
    
    const secondsEl = document.getElementById('countdownSeconds');
    const progressEl = document.getElementById('countdownProgress');
    
    if (!secondsEl || !progressEl) return;
    
    // If interval is 0, disable auto-refresh and show normal text
    if (intervalMinutes <= 0) {
        secondsEl.textContent = 'Reload';
        progressEl.style.strokeDashoffset = 100.53; // Hide progress ring
        countdownState.totalSeconds = 0;
        countdownState.seconds = 0;
        return;
    }
    
    const totalSeconds = intervalMinutes * 60;
    countdownState.totalSeconds = totalSeconds;
    countdownState.seconds = totalSeconds;
    
    // Circle circumference: 2 * PI * r = 2 * 3.14159 * 16 ≈ 100.53
    const circumference = 100.53;
    
    // Format time display
    const formatTime = (secs) => {
        if (secs >= 60) {
            const mins = Math.floor(secs / 60);
            const s = secs % 60;
            return `${mins}:${s.toString().padStart(2, '0')}`;
        }
        return `${secs}s`;
    };
    
    // Update every second
    countdownState.interval = setInterval(() => {
        countdownState.seconds--;
        
        // Update text
        secondsEl.textContent = formatTime(countdownState.seconds);
        
        // Update circle progress
        const progress = (countdownState.totalSeconds - countdownState.seconds) / countdownState.totalSeconds;
        progressEl.style.strokeDashoffset = circumference * progress;
        
        // When countdown reaches 0, reload quests
        if (countdownState.seconds <= 0) {
            loadQuests(false); // Don't show loading overlay for auto-refresh
            resetRefreshCountdown();
        }
    }, 1000);
    
    // Initial update
    secondsEl.textContent = formatTime(countdownState.seconds);
    progressEl.style.strokeDashoffset = 0;
}

// Reset countdown to initial value
function resetRefreshCountdown() {
    const secondsEl = document.getElementById('countdownSeconds');
    const progressEl = document.getElementById('countdownProgress');
    
    // If auto-refresh is disabled (interval = 0), show normal text
    if (countdownState.totalSeconds <= 0) {
        if (secondsEl) secondsEl.textContent = 'Reload';
        if (progressEl) progressEl.style.strokeDashoffset = 100.53;
        return;
    }
    
    countdownState.seconds = countdownState.totalSeconds;
    
    const formatTime = (secs) => {
        if (secs >= 60) {
            const mins = Math.floor(secs / 60);
            const s = secs % 60;
            return `${mins}:${s.toString().padStart(2, '0')}`;
        }
        return `${secs}s`;
    };
    
    if (secondsEl) secondsEl.textContent = formatTime(countdownState.seconds);
    if (progressEl) progressEl.style.strokeDashoffset = 0;
}

// Stop countdown
function stopRefreshCountdown() {
    if (countdownState.interval) {
        clearInterval(countdownState.interval);
        countdownState.interval = null;
    }
}

// Click on countdown to refresh immediately
function setupCountdownClick() {
    const countdownEl = document.getElementById('refreshCountdown');
    if (countdownEl) {
        countdownEl.addEventListener('click', () => {
            loadQuests(false);
            resetRefreshCountdown();
            showToast('Quests refreshed!', 'success');
        });
    }
}

// Accept a quest with button loading state
function acceptQuest(questId) {
    if (!questId) return;
    
    // Find and update the button
    const btn = document.querySelector(`[data-quest-id="${questId}"] .quest-accept-btn`);
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Accepting...';
        btn.classList.add('loading');
    }
    
    window.chrome.webview.postMessage({
        action: 'acceptQuest',
        questId: questId
    });
}

// Called when quest is accepted successfully
function questAccepted(data) {
    const { questId } = data;
    
    showToast('Quest accepted! 🎮', 'success');
    
    // Reload quests to update UI and badge counts (stay on current tab)
    loadQuests(false); // Silent reload
}

// Claim quest reward
function claimQuest(questId) {
    if (!questId) return;
    
    // Find and update the claim button
    const btn = document.querySelector(`[data-quest-id="${questId}"] .quest-claim-btn`);
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Claiming...';
        btn.classList.add('loading');
    }
    
    window.chrome.webview.postMessage({
        action: 'claimQuest',
        questId: questId
    });
}

// Called when claim requires captcha
function handleClaimCaptcha(data) {
    const { questId, message } = data;

    // Fun messages for captcha hint - updated for integrated solving
    const captchaHints = [
        "🤖 Solving captcha automatically...",
        "🎮 App is handling the captcha verification!",
        "🔐 Captcha solver activated!",
        "✨ Almost there! Solving captcha...",
        "🎁 Your reward is coming soon!",
        "🚀 Auto-solving captcha in progress!"
    ];
    const randomHint = captchaHints[Math.floor(Math.random() * captchaHints.length)];

    // Update card to show processing instead of button
    const card = document.querySelector(`[data-quest-id="${questId}"]`);
    if (card) {
        const btn = card.querySelector('.quest-claim-btn');
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Solving Captcha...';
            btn.classList.add('loading');
        }
    }

    showToast(randomHint, 'info');
}

// Open Discord quest page to claim
function openDiscordClaim(questId) {
    window.chrome.webview.postMessage({
        action: 'openDiscordClaim',
        questId: questId
    });
}

// Called when claim fails
function handleClaimFailed(data) {
    const { questId, message } = data;
    
    // Reset button
    const btn = document.querySelector(`[data-quest-id="${questId}"] .quest-claim-btn`);
    if (btn) {
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-gift"></i> Claim Reward';
        btn.classList.remove('loading');
    }
    
    showToast(`Claim failed: ${message}`, 'error');
}

// Called when claim succeeds
function handleClaimSuccess(data) {
    const { questId } = data;
    
    showToast('🎁 Reward claimed successfully!', 'success');
    
    // Update UI - replace claim button with "Claimed" status
    const card = document.querySelector(`[data-quest-id="${questId}"]`);
    if (card) {
        const claimBtn = card.querySelector('.quest-claim-btn');
        if (claimBtn) {
            claimBtn.remove();
        }
        const statusEl = card.querySelector('.quest-status');
        if (statusEl) {
            statusEl.innerHTML = '<i class="fas fa-gift"></i> Reward Claimed';
            statusEl.classList.remove('completed-status');
            statusEl.classList.add('claimed-status');
        }
    }
}

// Called when quest accept fails
function questAcceptError(data) {
    const { questId } = data;
    
    // Reset button state
    const btn = document.querySelector(`[data-quest-id="${questId}"] .quest-accept-btn`);
    if (btn) {
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-plus-circle"></i> Accept Quest';
        btn.classList.remove('loading');
    }
}

function renderQuests(quests) {
    allQuests = { ...allQuests, ...quests };
    renderQuestList('accepted', quests.accepted);
    renderQuestList('available', quests.available);
    renderQuestList('completed', quests.completed || []);

    // Update tab badges
    updateTabBadges();
    
    // Check for quests with active progress (detect if something is running)
    detectActiveQuests(quests.accepted);
    
    // Schedule a quick refresh to detect externally running quests
    scheduleExternalRunningDetection();
    
    // Start refresh countdown timer only if not already running
    if (!countdownState.interval) {
        const refreshInterval = parseInt(document.getElementById('txtRefreshInterval')?.value || '5');
        startRefreshCountdown(refreshInterval);
    }

    hideLoading();
    updateStatus(`Loaded ${quests.accepted.length} accepted and ${quests.available.length} available quests`);
}

// Schedule ONE external detection check after first load
let externalDetectionDone = false;
function scheduleExternalRunningDetection() {
    // Only run ONCE after first load, not repeatedly
    if (externalDetectionDone || questRunningState.isRunning) return;
    
    externalDetectionDone = true; // Never run again
    setTimeout(() => {
        if (!questRunningState.isRunning) {
            console.log('[External Detection] One-time check for externally running quests...');
            // Just detect, don't trigger another load - use countdown timer for that
            detectActiveQuests(allQuests.accepted || []);
        }
    }, 10000); // Check after 10 seconds
}

// Detect quests that have progress (simplified - no external tracking confusion)
function detectActiveQuests(acceptedQuests) {
    acceptedQuests.forEach(quest => {
        const progress = getQuestProgress(quest);
        const card = document.querySelector(`[data-quest-id="${quest.id}"]`);
        
        if (card && progress > 0 && progress < 100 && !questRunningState.isRunning) {
            // Quest has progress but not complete - show "In Progress"
            card.classList.add('quest-in-progress');
        }
    });
    
    // Check localStorage for saved running state (from previous session)
    const savedState = localStorage.getItem('runningQuestsState');
    
    if (savedState && !questRunningState.isRunning) {
        try {
            const state = JSON.parse(savedState);
            const stillRunning = state.questIds?.filter(id => {
                const quest = acceptedQuests.find(q => q.id === id);
                if (!quest) return false;
                const progress = getQuestProgress(quest);
                return progress > 0 && progress < 100;
            });
            
            if (stillRunning && stillRunning.length > 0) {
                showToast(`${stillRunning.length} quest(s) have progress. Click "Start" to continue.`, 'info');
                
                stillRunning.forEach(id => {
                    const card = document.querySelector(`[data-quest-id="${id}"]`);
                    if (card) {
                        card.classList.add('quest-was-running');
                    }
                });
            }
            
            localStorage.removeItem('runningQuestsState');
        } catch (e) {
            console.error('Failed to restore running state:', e);
        }
    }
}

// Helper to get quest progress percentage
function getQuestProgress(quest) {
    if (!quest.userStatus) return 0;
    
    const taskConfig = quest.config?.taskConfigV2 || quest.config?.taskConfig;
    if (!taskConfig?.tasks) return 0;
    
    // Find the task and calculate progress
    for (const [taskName, taskInfo] of Object.entries(taskConfig.tasks)) {
        const progressValue = quest.userStatus.progress?.[taskName]?.value 
            || quest.userStatus.streamProgressSeconds 
            || 0;
        const target = taskInfo.target || 1;
        return Math.min(100, Math.round((progressValue / target) * 100));
    }
    
    return 0;
}

// Update badge counts on tabs
function updateTabBadges() {
    const acceptedCount = allQuests.accepted?.length || 0;
    const availableCount = allQuests.available?.length || 0;
    const completedCount = allQuests.completed?.length || 0; // Unclaimed quests ready to claim
    const historyCount = allQuests.history?.length || 0;
    
    updateBadge('accepted', acceptedCount);
    updateBadge('available', availableCount);
    // Completed tab shows unclaimed quests - pulse if there are any!
    updateBadge('completed', completedCount, completedCount);
    updateBadge('history', historyCount);
}

// Update unclaimed badge indicator (for when history tab is loaded)
function updateUnclaimedBadge() {
    const completedCount = allQuests.completed?.length || 0;
    updateBadge('completed', completedCount, completedCount);
}

function updateBadge(tabName, count, unclaimedCount = 0) {
    const tab = document.querySelector(`[data-tab="${tabName}"]`);
    if (!tab) return;
    
    // Remove existing badge
    const existingBadge = tab.querySelector('.tab-badge');
    if (existingBadge) existingBadge.remove();
    
    // Add badge if count > 0
    if (count > 0) {
        const badge = document.createElement('span');
        badge.className = 'tab-badge';
        
        // Add pulse effect for unclaimed rewards
        if (tabName === 'completed' && unclaimedCount > 0) {
            badge.classList.add('tab-badge-unclaimed');
            badge.textContent = unclaimedCount > 99 ? '99+' : unclaimedCount;
            badge.title = `${unclaimedCount} reward(s) ready to claim!`;
        } else {
            badge.textContent = count > 99 ? '99+' : count;
        }
        
        tab.appendChild(badge);
    }
}

function renderHistoryQuests(data) {
    const { quests, hasMore, total } = data;
    const listEl = document.getElementById('historyQuestsList');
    const loadingMore = document.getElementById('historyLoadingMore');

    // Clear list on first page
    if (historyQuestsState.page === 0) {
        listEl.innerHTML = '';
        allQuests.history = [];
    }

    // Sort quests: unclaimed first, then claimed
    const sortedQuests = [...quests].sort((a, b) => {
        const aUnclaimed = !a.userStatus?.claimedAt;
        const bUnclaimed = !b.userStatus?.claimedAt;
        if (aUnclaimed && !bUnclaimed) return -1;
        if (!aUnclaimed && bUnclaimed) return 1;
        return 0;
    });

    // Append quests
    sortedQuests.forEach(quest => {
        allQuests.history.push(quest);
        const questCard = createQuestCardElement(quest, 'history');
        
        // Add special class for claimed vs unclaimed quests
        if (!quest.userStatus?.claimedAt) {
            questCard.classList.add('quest-unclaimed');
        } else {
            questCard.classList.add('quest-claimed');
        }
        
        listEl.appendChild(questCard);
    });

    // Update state
    historyQuestsState.loaded = true;
    historyQuestsState.hasMore = hasMore;
    historyQuestsState.loading = false;

    // Hide/show loading more indicator
    if (loadingMore) {
        if (hasMore) {
            loadingMore.classList.remove('hidden');
            loadingMore.textContent = 'Scroll for more...';
        } else {
            loadingMore.classList.add('hidden');
        }
    }

    // Update tab badges
    updateTabBadges();

    hideLoading();

    // Show no quests message if empty
    if (quests.length === 0 && historyQuestsState.page === 0) {
        listEl.innerHTML = '<div style="text-align: center; padding: 40px; color: #72767d;">No quest history found</div>';
    }

    if (historyQuestsState.page === 0) {
        updateStatus(`Loaded ${total || quests.length} history quests`);
    }
}

function createQuestCardElement(quest, type) {
    const card = document.createElement('div');
    card.className = 'quest-card';
    card.dataset.questId = quest.id;
    card.innerHTML = createQuestCard(quest, type);

    // Add click handler for selection (except completed and history)
    if (type !== 'completed' && type !== 'history') {
        card.addEventListener('click', () => toggleQuestSelection(quest.id));
    }

    return card;
}

function renderQuestList(type, quests) {
    const listEl = document.getElementById(`${type}QuestsList`);

    if (quests.length === 0) {
        listEl.innerHTML = `<div style="text-align: center; padding: 40px; color: #72767d;">No ${type} quests found</div>`;
        return;
    }

    listEl.innerHTML = quests.map(quest => createQuestCard(quest, type)).join('');

    // Add click handlers
    listEl.querySelectorAll('.quest-card').forEach(card => {
        card.addEventListener('click', () => toggleQuestSelection(card.dataset.questId));
    });
}

function createQuestCard(quest, type) {
    // Debug: log quest data
    console.log('Quest:', quest.id, 'Assets:', quest.config?.assets, 'Rewards:', quest.config?.rewardsConfig);
    
    // Null-safe access to task config
    const taskConfig = quest.config?.taskConfigV2 || quest.config?.taskConfig;
    const tasks = taskConfig?.tasks || {};
    const taskKeys = Object.keys(tasks);
    const taskName = taskKeys.length > 0 ? taskKeys[0] : 'Unknown';
    const taskInfo = tasks[taskName] || { target: 0 };
    const progress = quest.userStatus?.progress?.[taskName]?.value || 0;
    const target = taskInfo.target || 1;
    const percentage = Math.min(100, Math.round((progress / target) * 100));
    
    // For completed quests, show 100% and "Completed"
    const isCompleted = type === 'completed' || quest.userStatus?.completedAt;
    const isAccepted = type === 'accepted' || quest.userStatus?.enrolledAt;
    const displayPercentage = isCompleted ? 100 : percentage;
    
    // Calculate time info
    const targetMinutes = Math.ceil(target / 60);
    const progressMinutes = Math.floor(progress / 60);
    const taskDescription = getTaskDescription(taskName, targetMinutes);

    const isSelected = selectedQuests.has(quest.id);
    const selectedClass = isSelected ? ' selected' : '';
    const completedClass = isCompleted ? ' completed' : '';

    // Hero image URL - Discord uses cdn.discordapp.com/{asset_path}
    const heroAsset = quest.config?.assets?.hero || quest.config?.assets?.questBarHero;
    const heroUrl = heroAsset ? getDiscordAssetUrl(heroAsset) : '';
    
    // Game logo
    const logoAsset = quest.config?.assets?.logotype || quest.config?.assets?.gameTile;
    const logoUrl = logoAsset ? getDiscordAssetUrl(logoAsset) : '';
    
    // Rewards
    const rewards = quest.config?.rewardsConfig?.rewards || [];
    const rewardHtml = createRewardBadge(rewards);
    
    // Expiry date
    const expiresAt = quest.config?.expiresAt;
    const endsText = expiresAt ? formatExpiryDate(expiresAt) : '';
    
    // Publisher
    const publisher = quest.config?.messages?.gamePublisher || quest.config?.application?.name || '';
    
    // Quest type label
    const questTypeLabel = quest.config?.messages?.questName?.toUpperCase() + ' QUEST' || 'QUEST';

    // Check if reward is claimed
    const isClaimed = quest.userStatus?.claimedAt != null;
    
    // Status display based on quest state
    let statusHtml = '';
    if (isCompleted) {
        if (isClaimed) {
            // Already claimed
            statusHtml = `<div class="quest-status claimed-status"><i class="fas fa-gift"></i> Reward Claimed</div>`;
        } else {
            // Completed but not claimed - show claim button
            statusHtml = `
                <div class="quest-status completed-status"><i class="fas fa-check-circle"></i> Completed!</div>
                <button class="quest-claim-btn" onclick="event.stopPropagation(); claimQuest('${quest.id}')" data-quest-id="${quest.id}">
                    <i class="fas fa-gift"></i>
                    Claim Reward
                </button>
            `;
        }
    } else if (isAccepted) {
        statusHtml = `
            <div class="quest-progress-bar-container">
                <div class="quest-progress-bar-fill" style="width: ${displayPercentage}%"></div>
                <span class="quest-progress-text">${displayPercentage}% Complete</span>
            </div>
        `;
    } else {
        // Not accepted - show Accept button
        statusHtml = `
            <button class="quest-accept-btn" onclick="event.stopPropagation(); acceptQuest('${quest.id}')">
                <i class="fas fa-plus-circle"></i>
                Accept Quest
            </button>
        `;
    }

    // Quest title (reward name for title)
    const questTitle = getQuestTitle(rewards, quest.config?.messages?.questName);

    return `
        <div class="quest-card${selectedClass}${completedClass}" data-quest-id="${quest.id}">
            <div class="quest-hero-container">
                ${heroUrl ? `<img src="${heroUrl}" class="quest-hero-img" alt="" onerror="this.parentElement.innerHTML='<div class=quest-hero-placeholder></div><div class=quest-hero-overlay></div>'">` : '<div class="quest-hero-placeholder"></div>'}
                <div class="quest-hero-overlay"></div>
                ${logoUrl ? `<img src="${logoUrl}" class="quest-game-logo" alt="" onerror="this.style.display='none'">` : ''}
                ${rewardHtml}
            </div>
            <div class="quest-body">
                <div class="quest-meta">
                    <div class="quest-publisher">
                        <span>Promoted by</span>
                        <strong>${publisher}</strong>
                    </div>
                    ${endsText ? `<div class="quest-ends">Ends ${endsText}</div>` : ''}
                </div>
                <div class="quest-type-label">${questTypeLabel}</div>
                <div class="quest-title-large">${questTitle}</div>
                <div class="quest-description">${taskDescription}</div>
                ${statusHtml}
            </div>
        </div>
    `;
}

function getQuestTitle(rewards, defaultName) {
    if (!rewards || rewards.length === 0) return defaultName || 'Complete Quest';
    
    const reward = rewards[0];
    const rewardName = reward.messages?.name || 'Reward';
    
    if (reward.type === 4) {
        const orbQuantity = reward.orbQuantity || 0;
        return `Claim ${orbQuantity} Orbs`;
    }
    
    return `Claim ${rewardName}`;
}

function getDiscordAssetUrl(assetPath) {
    if (!assetPath) return '';
    
    // If already a full URL, return as-is
    if (assetPath.startsWith('http')) return assetPath;
    
    // Discord quest assets: quests/{quest_id}/{asset_id}.ext
    // URL format: https://cdn.discordapp.com/{asset_path}
    // Some assets might have different prefixes
    if (assetPath.includes('/')) {
        return `https://cdn.discordapp.com/${assetPath}`;
    }
    
    // Asset IDs without path (like game_tile: "1451697895706460332.png")
    // These might need different handling - try direct assets folder
    return `https://cdn.discordapp.com/app-assets/${assetPath}`;
}

function formatExpiryDate(dateStr) {
    const date = new Date(dateStr);
    const now = new Date();
    const diffDays = Math.ceil((date - now) / (1000 * 60 * 60 * 24));
    
    if (diffDays <= 0) return 'Today';
    if (diffDays === 1) return 'Tomorrow';
    if (diffDays <= 7) return `in ${diffDays} days`;
    
    return date.toLocaleDateString('en-US', { month: 'numeric', day: 'numeric' });
}

function getTaskDescription(taskName, targetMinutes) {
    const taskDescriptions = {
        'PLAY_ON_DESKTOP': `Play for ${targetMinutes} minutes with your Discord client open`,
        'PLAY_ON_PLAYSTATION': `Play for ${targetMinutes} minutes on PlayStation`,
        'PLAY_ON_XBOX': `Play for ${targetMinutes} minutes on Xbox`,
        'STREAM_ON_DESKTOP': `Stream for ${targetMinutes} minutes`,
        'WATCH_VIDEO': `Watch the video to complete`,
        'WATCH_VIDEO_ON_MOBILE': `Watch the video on mobile to complete`,
        'PLAY_ACTIVITY': `Play the activity for ${targetMinutes} minutes`
    };
    
    return taskDescriptions[taskName] || `Complete the task (${targetMinutes} min)`;
}

function formatTaskName(taskName) {
    // Convert PLAY_ON_DESKTOP to "Play on Desktop"
    return taskName.split('_').map(word => 
        word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()
    ).join(' ');
}

function createRewardBadge(rewards) {
    if (!rewards || rewards.length === 0) return '';
    
    const reward = rewards[0]; // Primary reward
    const name = reward.messages?.name || 'Reward';
    
    // Orb reward (type 4) - compact badge with number
    if (reward.type === 4) {
        const orbQuantity = reward.orbQuantity || 0;
        return `
            <div class="reward-badge reward-badge-orbs">
                <span class="reward-badge-text">${orbQuantity}</span>
                <svg class="reward-badge-orb" viewBox="0 0 20 20" width="18" height="18">
                    <defs>
                        <linearGradient id="orbGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                            <stop offset="0%" stop-color="#FFD700"/>
                            <stop offset="100%" stop-color="#FF8C00"/>
                        </linearGradient>
                    </defs>
                    <circle cx="10" cy="10" r="9" fill="url(#orbGrad)"/>
                    <circle cx="7" cy="7" r="2.5" fill="rgba(255,255,255,0.5)"/>
                </svg>
            </div>
        `;
    }
    
    // Other rewards with asset - circular preview like Discord
    const rewardAsset = reward.asset;
    if (rewardAsset) {
        const isVideo = rewardAsset.endsWith('.mp4') || rewardAsset.endsWith('.webm');
        const assetUrl = getDiscordAssetUrl(rewardAsset);
        
        return `
            <div class="reward-badge reward-badge-avatar" title="${name}">
                <div class="reward-avatar-circle">
                    ${isVideo ? `
                        <video src="${assetUrl}" autoplay loop muted playsinline class="reward-avatar-media"></video>
                    ` : `
                        <img src="${assetUrl}" class="reward-avatar-media" alt="${name}" onerror="this.src='https://cdn.discordapp.com/embed/avatars/0.png'">
                    `}
                </div>
            </div>
        `;
    }
    
    // Fallback - Discord icon
    return `
        <div class="reward-badge reward-badge-avatar" title="${name}">
            <div class="reward-avatar-circle reward-avatar-default">
                <i class="fab fa-discord"></i>
            </div>
        </div>
    `;
}

function createProgressCircle(percentage) {
    const radius = 36;
    const circumference = 2 * Math.PI * radius;
    const offset = circumference - (percentage / 100) * circumference;

    return `
        <div class="quest-progress">
            <svg width="80" height="80">
                <circle class="quest-progress-circle" cx="40" cy="40" r="${radius}" />
                <circle class="quest-progress-bar" cx="40" cy="40" r="${radius}"
                    stroke-dasharray="${circumference}"
                    stroke-dashoffset="${offset}" />
            </svg>
            <div class="quest-progress-text">${percentage}%</div>
        </div>
    `;
}

function toggleQuestSelection(questId) {
    const card = document.querySelector(`[data-quest-id="${questId}"]`);

    if (selectedQuests.has(questId)) {
        selectedQuests.delete(questId);
        card?.classList.remove('selected');
    } else {
        selectedQuests.add(questId);
        card?.classList.add('selected');
    }
}

function startSelectedQuests() {
    if (selectedQuests.size === 0) {
        showToast('Please select quests to start', 'warning');
        updateStatus('No quests selected');
        return;
    }

    const questIds = Array.from(selectedQuests);
    startQuests(questIds);
}

// Start all accepted quests
function startAllAcceptedQuests() {
    // Get incomplete accepted quests
    const incompleteQuests = allQuests.accepted
        .filter(q => !q.userStatus?.completedAt);
    
    if (incompleteQuests.length === 0) {
        showToast('No accepted quests to start', 'warning');
        return;
    }
    
    // Sort by progress (highest first) to complete in-progress quests first
    const sortedQuests = incompleteQuests.sort((a, b) => {
        const progressA = getQuestProgress(a);
        const progressB = getQuestProgress(b);
        return progressB - progressA; // Descending: highest progress first
    });
    
    const sortedQuestIds = sortedQuests.map(q => q.id);
    
    // Log for debugging
    console.log('[Start All] Prioritized order:', sortedQuests.map(q => ({
        name: q.config?.messages?.questName,
        progress: getQuestProgress(q) + '%'
    })));
    
    // Show toast with priority info
    const firstQuest = sortedQuests[0];
    const firstProgress = getQuestProgress(firstQuest);
    if (firstProgress > 0) {
        showToast(`Starting with "${firstQuest.config?.messages?.questName}" (${firstProgress}% done)`, 'info');
    }
    
    startQuests(sortedQuestIds);
}

// Common function to start quests
function startQuests(questIds) {
    if (!questIds || questIds.length === 0) return;
    
    window.chrome.webview.postMessage({
        action: 'startQuests',
        questIds: questIds
    });

    // Show running banner instead of loading overlay
    showQuestRunningBanner(questIds);
}

// Stop all running quests
function stopAllQuests() {
    window.chrome.webview.postMessage({ action: 'stopQuests' });
    hideQuestRunningBanner();
    showToast('Stopping all quests...', 'info');
}

// Show the running banner with fun status messages
function showQuestRunningBanner(questIds) {
    const banner = document.getElementById('questRunningBanner');
    const countEl = document.getElementById('runningQuestCount');
    
    if (banner) {
        banner.classList.remove('hidden');
    }
    if (countEl) {
        countEl.textContent = `${questIds.length} quest(s)`;
    }
    
    // Update running state
    questRunningState.isRunning = true;
    questRunningState.runningQuests = questIds;
    
    // Save to localStorage so we can restore if app closes
    localStorage.setItem('runningQuestsState', JSON.stringify({
        questIds: questIds,
        startedAt: Date.now()
    }));
    
    // Mark quests as running in UI
    questIds.forEach(id => {
        const card = document.querySelector(`[data-quest-id="${id}"]`);
        if (card) {
            card.classList.add('quest-active');
        }
    });
    
    // Set current quest (first one)
    if (questIds.length > 0) {
        setCurrentRunningQuest(questIds[0]);
    }
    
    // Start rotating fun status messages
    startStatusRotation();
    
    hideLoading();
}

// Hide the running banner
function hideQuestRunningBanner() {
    const banner = document.getElementById('questRunningBanner');
    if (banner) {
        banner.classList.add('hidden');
    }
    
    // Clear running state
    questRunningState.isRunning = false;
    
    // Remove running class from all cards
    questRunningState.runningQuests.forEach(id => {
        const card = document.querySelector(`[data-quest-id="${id}"]`);
        if (card) {
            card.classList.remove('quest-active', 'quest-current');
        }
    });
    
    questRunningState.runningQuests = [];
    questRunningState.currentQuestId = null;
    
    // Clear saved state
    localStorage.removeItem('runningQuestsState');
    
    // Stop status rotation
    stopStatusRotation();
}

// Set current running quest (with visual indicator)
function setCurrentRunningQuest(questId) {
    // Remove current from previous
    if (questRunningState.currentQuestId) {
        const prevCard = document.querySelector(`[data-quest-id="${questRunningState.currentQuestId}"]`);
        if (prevCard) {
            prevCard.classList.remove('quest-current');
        }
    }
    
    // Set new current
    questRunningState.currentQuestId = questId;
    const card = document.querySelector(`[data-quest-id="${questId}"]`);
    if (card) {
        card.classList.add('quest-current');
        
        // Scroll into view if not visible
        card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
}

// Status message rotation
function startStatusRotation() {
    updateRunningStatus(); // Immediate first update
    
    if (questRunningState.statusInterval) {
        clearInterval(questRunningState.statusInterval);
    }
    
    questRunningState.statusInterval = setInterval(() => {
        updateRunningStatus();
    }, 8000); // Every 8 seconds
}

function stopStatusRotation() {
    if (questRunningState.statusInterval) {
        clearInterval(questRunningState.statusInterval);
        questRunningState.statusInterval = null;
    }
}

function updateRunningStatus() {
    const statusEl = document.getElementById('runningStatusText');
    if (!statusEl) return;
    
    const randomIndex = Math.floor(Math.random() * funStatusMessages.length);
    statusEl.textContent = funStatusMessages[randomIndex];
    
    // Add animation class
    statusEl.classList.remove('status-fade');
    void statusEl.offsetWidth; // Trigger reflow
    statusEl.classList.add('status-fade');
}

function updateQuestProgress(data) {
    console.log('updateQuestProgress called:', data);
    const { questId, percentage, secondsRemaining } = data;
    const card = document.querySelector(`[data-quest-id="${questId}"]`);
    console.log('Found card:', card ? 'yes' : 'no', 'for questId:', questId);
    
    if (!card) return;

    // Update progress bar (linear style)
    const progressFill = card.querySelector('.quest-progress-bar-fill');
    const progressText = card.querySelector('.quest-progress-text');
    
    console.log('progressFill:', progressFill ? 'found' : 'not found');
    console.log('progressText:', progressText ? 'found' : 'not found');

    if (progressFill) {
        progressFill.style.width = `${percentage}%`;
        console.log('Set width to:', `${percentage}%`);
    }
    if (progressText) {
        progressText.textContent = `${percentage}% Complete`;
    }

    // Mark as current running quest
    if (questRunningState.currentQuestId !== questId) {
        setCurrentRunningQuest(questId);
    }

    // Update quest count in banner if needed
    const countEl = document.getElementById('runningQuestCount');
    if (countEl && questRunningState.runningQuests.length > 0) {
        const remaining = questRunningState.runningQuests.filter(id => {
            const c = document.querySelector(`[data-quest-id="${id}"]`);
            return c && !c.classList.contains('quest-completed');
        }).length;
        countEl.textContent = `${remaining} quest(s) running`;
    }
    
    // Update status bar too
    updateStatus(`Quest progress: ${percentage}%`);
}

function questCompleted(data) {
    const { questId } = data;
    const card = document.querySelector(`[data-quest-id="${questId}"]`);
    if (card) {
        card.classList.remove('quest-active', 'quest-current', 'selected');
        card.classList.add('quest-completed');
        selectedQuests.delete(questId);
    }

    // Remove from running quests
    questRunningState.runningQuests = questRunningState.runningQuests.filter(id => id !== questId);
    
    // Reload quests to update UI - completed quests will be included automatically now
    loadQuests(false);
    
    // Reset history state so it will reload fresh when user opens History tab
    historyQuestsState = { page: 0, pageSize: 5, hasMore: true, loading: false, loaded: false };
    
    // Add pulse to Completed tab to notify user
    const completedTab = document.querySelector('[data-tab="completed"]');
    if (completedTab) {
        completedTab.classList.add('tab-pulse');
        setTimeout(() => completedTab.classList.remove('tab-pulse'), 3000);
    }
    
    // If there are more quests, set next one as current
    if (questRunningState.runningQuests.length > 0) {
        setCurrentRunningQuest(questRunningState.runningQuests[0]);
        showToast(`Quest completed! ${questRunningState.runningQuests.length} remaining...`, 'success');
    } else {
        // All quests done!
        hideQuestRunningBanner();
        showToast('🎉 All quests completed!', 'success');
        updateStatus('All quests completed!');
    }
}

// Called when quests start running from backend
function questsStarted(data) {
    const { questIds } = data;
    if (questIds && questIds.length > 0) {
        showQuestRunningBanner(questIds);
    }
}

// Called when all quests stopped
function questsStopped() {
    hideQuestRunningBanner();
    updateStatus('Quests stopped');
}

function showSettings() {
    document.getElementById('settingsModal')?.classList.remove('hidden');

    // Load current settings
    window.chrome.webview.postMessage({ action: 'getSettings' });
}

function hideSettings() {
    document.getElementById('settingsModal')?.classList.add('hidden');
}

function loadSettings(settings) {
    const chkNotifications = document.getElementById('chkNotifications');
    const txtRefreshInterval = document.getElementById('txtRefreshInterval');

    if (chkNotifications) chkNotifications.checked = settings.enableNotifications;
    if (txtRefreshInterval) txtRefreshInterval.value = settings.autoRefreshInterval;
}

function saveSettings() {
    const chkNotifications = document.getElementById('chkNotifications');
    const txtRefreshInterval = document.getElementById('txtRefreshInterval');

    const newInterval = parseInt(txtRefreshInterval?.value || '5');
    
    const settings = {
        enableNotifications: chkNotifications?.checked || false,
        autoRefreshInterval: newInterval
    };

    window.chrome.webview.postMessage({
        action: 'saveSettings',
        settings: settings
    });

    // Update countdown with new interval
    startRefreshCountdown(newInterval);

    hideSettings();
    showToast('Settings saved', 'success');
    updateStatus('Settings saved');
}

function logout() {
    if (confirm('Are you sure you want to logout?')) {
        window.chrome.webview.postMessage({ action: 'logout' });
        showToast('Logging out...', 'info');
    }
}

function updateStatus(message) {
    const statusEl = document.getElementById('statusText');
    if (statusEl) statusEl.textContent = message;
}

function handleError(error) {
    const message = error.message || error.toString();
    hideLoading();
    showToast(message, 'error');
    updateStatus(`Error: ${message}`);

    // Reset login button if on login screen
    const btnLogin = document.getElementById('btnLogin');
    const loginScreen = document.getElementById('loginScreen');
    if (btnLogin && !loginScreen.classList.contains('hidden')) {
        setButtonState(btnLogin, 'normal', 'Login with Discord');
    }
}

// Listen for messages from C#
window.chrome.webview.addEventListener('message', (event) => {
    const message = event.data;

    switch (message.type) {
        case 'showLoginScreen':
            showLoginScreen();
            break;
        case 'loginSuccess':
            loginSuccess(message.data);
            break;
        case 'questsLoaded':
            renderQuests(message.data);
            break;
        case 'historyQuestsLoaded':
            renderHistoryQuests(message.data);
            break;
        case 'questsStarted':
            questsStarted(message.data);
            break;
        case 'questAccepted':
            questAccepted(message.data);
            break;
        case 'questAcceptError':
            questAcceptError(message.data);
            break;
        case 'questClaimed':
            handleClaimSuccess(message.data);
            break;
        case 'claimRequiresCaptcha':
            handleClaimCaptcha(message.data);
            break;
        case 'claimFailed':
            handleClaimFailed(message.data);
            break;
        case 'questProgress':
            updateQuestProgress(message.data);
            break;
        case 'questCompleted':
            questCompleted(message.data);
            break;
        case 'questsStopped':
            questsStopped();
            break;
        case 'settingsLoaded':
            loadSettings(message.data);
            break;
        case 'toast':
            showToast(message.data.message, message.data.type);
            break;
        case 'loading':
            if (message.data.isLoading) {
                showLoading(message.data.message);
            } else {
                hideLoading();
            }
            break;
        case 'error':
            handleError(message.data);
            hideQuestRunningBanner();
            break;
    }
});
