let selectedRunes = [];
let gameState = null;

const maxPlayerHp = 50;
const maxMonsterHp = 100;

document.addEventListener('DOMContentLoaded', () => {
    fetchState();
});

async function fetchState() {
    try {
        const response = await fetch('/api/state');
        gameState = await response.json();
        updateUI();
    } catch (e) {
        console.error("Failed to fetch initial state:", e);
    }
}

async function resetGame() {
    try {
        const response = await fetch('/api/reset', { method: 'POST' });
        gameState = await response.json();
        selectedRunes = [];
        document.getElementById('combat-log').innerHTML = '<div class="log-entry system-msg">Game restarted.</div>';
        document.getElementById('game-over-overlay').classList.remove('active');
        updateRuneDisplay();
        updateUI();
    } catch (e) {
        console.error("Failed to reset game:", e);
    }
}

function addRune(runeType) {
    if (gameState && (gameState.playerHp <= 0 || gameState.monsterHp <= 0)) return;

    if (selectedRunes.length < 3) {
        selectedRunes.push(runeType);
        updateRuneDisplay();

        if (selectedRunes.length === 3) {
            castSpell();
        }
    }
}

function updateRuneDisplay() {
    const slots = document.querySelectorAll('.rune-slot');
    slots.forEach((slot, index) => {
        if (index < selectedRunes.length) {
            const r = selectedRunes[index];
            slot.className = `rune-slot filled ${r}`;
            slot.textContent = r;
        } else {
            slot.className = 'rune-slot';
            slot.textContent = '';
        }
    });
}

async function castSpell() {
    try {
        const payload = { Runes: selectedRunes };
        
        // Disable buttons temporarily
        document.querySelectorAll('.rune-btn').forEach(b => b.disabled = true);
        
        const response = await fetch('/api/cast', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        
        const result = await response.json();
        const logContainer = document.getElementById('combat-log');
        
        // Process each event sequentially for animation
        for (let i = 0; i < result.logs.length; i++) {
            const log = result.logs[i];
            
            // Update state dynamically based on the intermediate event state
            gameState = log.stateAfter;
            updateUI();
            
            // Append logs
            const entry = document.createElement('div');
            entry.className = `log-entry ${log.actor}`;
            entry.textContent = `[${log.actor}] ${log.message}`;
            logContainer.appendChild(entry);
            
            // Auto-scroll log
            logContainer.scrollTop = logContainer.scrollHeight;
            
            // Wait 1 second before the next action for animation effect
            if (i < result.logs.length - 1) {
                await new Promise(resolve => setTimeout(resolve, 1000));
            }
        }
        
        // Final fallback state update
        gameState = result.state;
        updateUI();

        // Reset runes for next turn
        selectedRunes = [];
        updateRuneDisplay();
        document.querySelectorAll('.rune-btn').forEach(b => b.disabled = false);
        checkGameOver();

    } catch (e) {
        console.error("Failed to cast spell:", e);
        document.querySelectorAll('.rune-btn').forEach(b => b.disabled = false);
    }
}

function updateUI() {
    if (!gameState) return;

    // Player HP
    const pBar = document.getElementById('player-hp-bar');
    const pText = document.getElementById('player-hp-text');
    const pPercent = (gameState.playerHp / maxPlayerHp) * 100;
    pBar.style.width = `${pPercent}%`;
    pBar.style.backgroundColor = getHpColor(pPercent);
    pText.textContent = `${gameState.playerHp}/${maxPlayerHp}`;

    // Monster HP
    const mBar = document.getElementById('monster-hp-bar');
    const mText = document.getElementById('monster-hp-text');
    const mPercent = (gameState.monsterHp / maxMonsterHp) * 100;
    mBar.style.width = `${mPercent}%`;
    mBar.style.backgroundColor = getHpColor(mPercent);
    mText.textContent = `${gameState.monsterHp}/${maxMonsterHp}`;

    // Status
    const statusDiv = document.getElementById('monster-status');
    statusDiv.textContent = gameState.monsterStatus;
    statusDiv.className = `status-container status-${gameState.monsterStatus}`;
}

function getHpColor(percent) {
    if (percent > 50) return 'var(--hp-green)';
    if (percent > 20) return '#eab308'; // Yellow
    return '#ef4444'; // Red
}

function checkGameOver() {
    if (!gameState) return;

    if (gameState.playerHp <= 0 || gameState.monsterHp <= 0) {
        const overlay = document.getElementById('game-over-overlay');
        const title = document.getElementById('overlay-title');
        
        if (gameState.monsterHp <= 0 && gameState.playerHp > 0) {
            title.textContent = "Victory!";
            title.style.color = "var(--hp-green)";
        } else if (gameState.playerHp <= 0) {
            title.textContent = "Game Over";
            title.style.color = "var(--color-fire)";
        } else {
            title.textContent = "Draw!";
            title.style.color = "var(--text-secondary)";
        }
        
        overlay.classList.add('active');
    }
}
