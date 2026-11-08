/**
 * Episode Limit Plugin - Player UI Integration
 * Adds a timer icon to the video player controls that lets you set
 * how many episodes to play before automatically stopping.
 */
(function() {
    'use strict';

    // Plugin state
    let currentLimit = 0;
    let episodesPlayed = 0;
    let isActive = false;
    let statusCheckInterval = null;

    // Create the timer button for the player controls
    function createTimerButton() {
        const button = document.createElement('button');
        button.id = 'episodeLimitBtn';
        button.className = 'paper-icon-button-light btnEpisodeLimit';
        button.title = 'Episode Limit - Sleep Timer';
        button.setAttribute('is', 'paper-icon-button-light');
        
        // Clock/timer icon (SVG)
        button.innerHTML = `
            <span class="material-icons episodeLimitIcon" style="font-size: 24px;">bedtime</span>
        `;
        
        button.addEventListener('click', showLimitDialog);
        
        return button;
    }

    // Create the popup dialog for setting the limit
    function createDialog() {
        // Check if dialog already exists
        if (document.getElementById('episodeLimitDialog')) {
            return document.getElementById('episodeLimitDialog');
        }

        const dialog = document.createElement('div');
        dialog.id = 'episodeLimitDialog';
        dialog.innerHTML = `
            <div class="episodeLimitOverlay" id="episodeLimitOverlay"></div>
            <div class="episodeLimitModal">
                <div class="episodeLimitHeader">
                    <span class="material-icons" style="margin-right: 10px;">bedtime</span>
                    Episode Limit
                </div>
                <div class="episodeLimitBody">
                    <p class="episodeLimitDesc">Stop playback after this many episodes:</p>
                    
                    <div class="episodeLimitQuickButtons">
                        <button class="quickBtn" data-value="0">Off</button>
                        <button class="quickBtn" data-value="1">1</button>
                        <button class="quickBtn" data-value="2">2</button>
                        <button class="quickBtn" data-value="3">3</button>
                        <button class="quickBtn" data-value="4">4</button>
                        <button class="quickBtn" data-value="5">5</button>
                    </div>
                    
                    <div class="episodeLimitCustom">
                        <label for="episodeLimitInput">Custom:</label>
                        <input type="number" id="episodeLimitInput" min="1" max="100" value="3" />
                        <button class="customSetBtn" id="episodeLimitCustomSet">Set</button>
                    </div>
                    
                    <div class="episodeLimitStatus" id="episodeLimitStatus">
                        <!-- Status will be updated dynamically -->
                    </div>
                    
                    <div class="episodeLimitTimeRef">
                        <small>Counts completed files — works with any length media!</small>
                    </div>
                </div>
                <div class="episodeLimitFooter">
                    <button class="episodeLimitClose" id="episodeLimitClose">Close</button>
                </div>
            </div>
        `;

        // Add styles
        const style = document.createElement('style');
        style.textContent = `
            .episodeLimitOverlay {
                position: fixed;
                top: 0;
                left: 0;
                right: 0;
                bottom: 0;
                background: rgba(0, 0, 0, 0.7);
                z-index: 9998;
            }
            .episodeLimitModal {
                position: fixed;
                top: 50%;
                left: 50%;
                transform: translate(-50%, -50%);
                background: #1a1a2e;
                border-radius: 12px;
                padding: 0;
                z-index: 9999;
                min-width: 320px;
                max-width: 90vw;
                box-shadow: 0 10px 40px rgba(0, 0, 0, 0.5);
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            }
            .episodeLimitHeader {
                background: #00a4dc;
                color: white;
                padding: 15px 20px;
                font-size: 18px;
                font-weight: 600;
                border-radius: 12px 12px 0 0;
                display: flex;
                align-items: center;
            }
            .episodeLimitBody {
                padding: 20px;
            }
            .episodeLimitDesc {
                margin: 0 0 15px 0;
                color: #ccc;
                font-size: 14px;
            }
            .episodeLimitQuickButtons {
                display: flex;
                gap: 8px;
                flex-wrap: wrap;
                margin-bottom: 15px;
            }
            .quickBtn {
                flex: 1;
                min-width: 45px;
                padding: 12px 8px;
                border: 2px solid #333;
                background: #252542;
                color: #fff;
                border-radius: 8px;
                font-size: 16px;
                font-weight: 600;
                cursor: pointer;
                transition: all 0.2s;
            }
            .quickBtn:hover {
                background: #333355;
                border-color: #00a4dc;
            }
            .quickBtn.active {
                background: #00a4dc;
                border-color: #00a4dc;
            }
            .episodeLimitCustom {
                display: flex;
                align-items: center;
                gap: 10px;
                margin-bottom: 15px;
                padding: 10px;
                background: #252542;
                border-radius: 8px;
            }
            .episodeLimitCustom label {
                color: #aaa;
                font-size: 14px;
            }
            .episodeLimitCustom input {
                width: 60px;
                padding: 8px;
                border: 1px solid #444;
                background: #1a1a2e;
                color: #fff;
                border-radius: 4px;
                font-size: 16px;
                text-align: center;
            }
            .customSetBtn {
                padding: 8px 16px;
                background: #00a4dc;
                border: none;
                color: white;
                border-radius: 4px;
                cursor: pointer;
                font-weight: 600;
            }
            .customSetBtn:hover {
                background: #0088bb;
            }
            .episodeLimitStatus {
                padding: 12px;
                background: #252542;
                border-radius: 8px;
                margin-bottom: 10px;
                font-size: 14px;
            }
            .statusActive {
                color: #4caf50;
            }
            .statusInactive {
                color: #888;
            }
            .episodeLimitTimeRef {
                color: #666;
                text-align: center;
            }
            .episodeLimitFooter {
                padding: 15px 20px;
                border-top: 1px solid #333;
                text-align: right;
            }
            .episodeLimitClose {
                padding: 10px 24px;
                background: #333;
                border: none;
                color: white;
                border-radius: 6px;
                cursor: pointer;
                font-size: 14px;
            }
            .episodeLimitClose:hover {
                background: #444;
            }
            
            /* Button indicator when active */
            .btnEpisodeLimit.limitActive .episodeLimitIcon {
                color: #4caf50 !important;
            }
            
            #episodeLimitDialog {
                display: none;
            }
            #episodeLimitDialog.visible {
                display: block;
            }
        `;
        
        document.head.appendChild(style);
        document.body.appendChild(dialog);

        // Event listeners
        dialog.querySelector('#episodeLimitOverlay').addEventListener('click', hideDialog);
        dialog.querySelector('#episodeLimitClose').addEventListener('click', hideDialog);
        
        dialog.querySelectorAll('.quickBtn').forEach(btn => {
            btn.addEventListener('click', function() {
                setLimit(parseInt(this.dataset.value, 10));
            });
        });
        
        dialog.querySelector('#episodeLimitCustomSet').addEventListener('click', function() {
            const value = parseInt(dialog.querySelector('#episodeLimitInput').value, 10);
            if (value >= 1 && value <= 100) {
                setLimit(value);
            }
        });

        return dialog;
    }

    // Show the dialog
    function showLimitDialog(e) {
        e.preventDefault();
        e.stopPropagation();
        
        const dialog = createDialog();
        dialog.classList.add('visible');
        
        // Fetch current status
        fetchStatus();
        
        // Pause video interaction while dialog is open
        document.body.style.overflow = 'hidden';
    }

    // Hide the dialog
    function hideDialog() {
        const dialog = document.getElementById('episodeLimitDialog');
        if (dialog) {
            dialog.classList.remove('visible');
        }
        document.body.style.overflow = '';
    }

    // Update the status display
    function updateStatusDisplay() {
        const statusEl = document.getElementById('episodeLimitStatus');
        const quickBtns = document.querySelectorAll('.quickBtn');
        const button = document.getElementById('episodeLimitBtn');
        
        if (statusEl) {
            if (isActive && currentLimit > 0) {
                statusEl.innerHTML = `
                    <span class="statusActive">● Active</span> - 
                    Will stop after <strong>${currentLimit}</strong> episode${currentLimit !== 1 ? 's' : ''}<br>
                    <small>Episodes played: ${episodesPlayed} / ${currentLimit}</small>
                `;
            } else {
                statusEl.innerHTML = `<span class="statusInactive">● Inactive</span> - Playback will continue indefinitely`;
            }
        }
        
        // Update quick button states
        quickBtns.forEach(btn => {
            const val = parseInt(btn.dataset.value, 10);
            btn.classList.toggle('active', isActive && val === currentLimit);
            if (val === 0) {
                btn.classList.toggle('active', !isActive || currentLimit === 0);
            }
        });
        
        // Update player button indicator
        if (button) {
            button.classList.toggle('limitActive', isActive && currentLimit > 0);
        }
    }

    // Fetch current status from API
    async function fetchStatus() {
        try {
            const response = await ApiClient.fetch({
                url: ApiClient.getUrl('EpisodeLimit/Status'),
                type: 'GET'
            });
            
            currentLimit = response.Limit || 0;
            episodesPlayed = response.EpisodesPlayed || 0;
            isActive = response.IsActive || false;
            
            updateStatusDisplay();
        } catch (err) {
            console.error('Episode Limit: Failed to fetch status', err);
        }
    }

    // Set the episode limit via API
    async function setLimit(limit) {
        try {
            const response = await ApiClient.fetch({
                url: ApiClient.getUrl('EpisodeLimit/SetLimit'),
                type: 'POST',
                data: JSON.stringify({ Limit: limit }),
                contentType: 'application/json'
            });
            
            currentLimit = response.Limit || 0;
            episodesPlayed = response.EpisodesPlayed || 0;
            isActive = response.IsActive || false;
            
            updateStatusDisplay();
            
            // Show confirmation
            if (limit > 0) {
                Dashboard.alert(`Episode limit set to ${limit}. Sweet dreams! 😴`);
            } else {
                Dashboard.alert('Episode limit disabled.');
            }
            
            hideDialog();
        } catch (err) {
            console.error('Episode Limit: Failed to set limit', err);
            Dashboard.alert('Failed to set episode limit. Please try again.');
        }
    }

    // Inject the button into the video player controls
    function injectButton() {
        // Look for the video OSD (on-screen display) controls
        const osdControls = document.querySelector('.videoOsdBottom .buttons');
        
        if (osdControls && !document.getElementById('episodeLimitBtn')) {
            const button = createTimerButton();
            
            // Insert before the last few buttons (usually fullscreen, etc.)
            const children = osdControls.children;
            if (children.length > 2) {
                osdControls.insertBefore(button, children[children.length - 2]);
            } else {
                osdControls.appendChild(button);
            }
            
            console.log('Episode Limit: Button injected into player');
            
            // Start status checking while playing
            if (statusCheckInterval) clearInterval(statusCheckInterval);
            statusCheckInterval = setInterval(fetchStatus, 30000); // Check every 30s
            fetchStatus(); // Initial fetch
        }
    }

    // Clean up when leaving the player
    function cleanup() {
        if (statusCheckInterval) {
            clearInterval(statusCheckInterval);
            statusCheckInterval = null;
        }
    }

    // Watch for player appearing/disappearing
    function setupObserver() {
        const observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.addedNodes.length) {
                    // Check if video player appeared
                    if (document.querySelector('.videoOsdBottom')) {
                        setTimeout(injectButton, 500); // Small delay for controls to fully render
                    }
                }
                if (mutation.removedNodes.length) {
                    // Check if video player was removed
                    mutation.removedNodes.forEach(function(node) {
                        if (node.classList && node.classList.contains('videoOsdBottom')) {
                            cleanup();
                        }
                    });
                }
            });
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    // Initialize when Jellyfin is ready
    function init() {
        console.log('Episode Limit: Initializing player UI');
        setupObserver();
        
        // Also try to inject immediately if player is already visible
        if (document.querySelector('.videoOsdBottom')) {
            setTimeout(injectButton, 500);
        }
    }

    // Wait for Jellyfin to be ready
    if (window.ApiClient) {
        init();
    } else {
        document.addEventListener('viewshow', function() {
            if (window.ApiClient) {
                init();
            }
        });
    }

})();
// ---- Episode Limit v2: one-time welcome message ----
// Shows once per browser, the first time v2 becomes active. If you can
// see this popup, the plugin loaded and the injector worked. Its
// silence afterwards is a feature, not a bug.
(function () {
    "use strict";
    var FLAG = "episodelimitV2Welcomed";
    try {
        if (window.localStorage.getItem(FLAG) === "1") { return; }
        window.localStorage.setItem(FLAG, "1");
    } catch (err) {
        return; // localStorage blocked (private mode): stay quiet
    }

    function showWelcome() {
        if (document.getElementById("episodelimit-welcome")) { return; }

        var box = document.createElement("div");
        box.id = "episodelimit-welcome";
        box.style.cssText =
            "position:fixed;bottom:24px;right:24px;z-index:99999;max-width:340px;" +
            "padding:16px 18px;border-radius:10px;background:#101828;color:#eeeeee;" +
            "border:1px solid #3a4a5a;font-size:14px;line-height:1.4;" +
            "box-shadow:0 6px 24px rgba(0,0,0,0.5);";

        var title = document.createElement("div");
        title.textContent = "🌙 Episode Limit v2 is active";
        title.style.cssText = "font-weight:600;margin-bottom:6px;";

        var body = document.createElement("div");
        body.textContent =
            "Look for the moon button in the player controls to set your bedtime limit.";

        var ok = document.createElement("button");
        ok.type = "button";
        ok.textContent = "OK";
        ok.style.cssText =
            "margin-top:12px;padding:6px 24px;border:0;border-radius:6px;" +
            "background:#00a4dc;color:#ffffff;font-size:14px;cursor:pointer;";
        ok.addEventListener("click", function () { box.remove(); });

        box.appendChild(title);
        box.appendChild(body);
        box.appendChild(ok);
        document.body.appendChild(box);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", showWelcome);
    } else {
        showWelcome();
    }
})();
