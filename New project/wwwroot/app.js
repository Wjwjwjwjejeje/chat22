const TOKEN_KEY = "gmailChatBackendToken";
const POLL_MS = 2000;

const elements = {
  authPanel: document.getElementById("authPanel"),
  chatPanel: document.getElementById("chatPanel"),
  authMessage: document.getElementById("authMessage"),
  signupForm: document.getElementById("signupForm"),
  loginForm: document.getElementById("loginForm"),
  signupEmail: document.getElementById("signupEmail"),
  signupUsername: document.getElementById("signupUsername"),
  signupPassword: document.getElementById("signupPassword"),
  loginUsername: document.getElementById("loginUsername"),
  loginPassword: document.getElementById("loginPassword"),
  welcomeText: document.getElementById("welcomeText"),
  onlineUsers: document.getElementById("onlineUsers"),
  leaderboard: document.getElementById("leaderboard"),
  chatMessages: document.getElementById("chatMessages"),
  messageForm: document.getElementById("messageForm"),
  messageInput: document.getElementById("messageInput"),
  logoutButton: document.getElementById("logoutButton"),
  tabButtons: document.querySelectorAll(".tab-button"),
  authForms: document.querySelectorAll(".auth-form")
};

let pollHandle = null;

function getToken() {
  return localStorage.getItem(TOKEN_KEY);
}

function setToken(token) {
  if (token) {
    localStorage.setItem(TOKEN_KEY, token);
    return;
  }

  localStorage.removeItem(TOKEN_KEY);
}

function setStatus(message, type = "") {
  elements.authMessage.textContent = message;
  elements.authMessage.className = `status-message ${type}`.trim();
}

function switchTab(tabName) {
  elements.tabButtons.forEach((button) => {
    button.classList.toggle("active", button.dataset.tab === tabName);
  });

  elements.authForms.forEach((form) => {
    form.classList.toggle("active", form.id === `${tabName}Form`);
  });

  setStatus("");
}

function escapeHtml(value) {
  const div = document.createElement("div");
  div.textContent = value;
  return div.innerHTML;
}

function formatTime(isoString) {
  return new Date(isoString).toLocaleString([], {
    dateStyle: "medium",
    timeStyle: "short"
  });
}

async function api(path, options = {}) {
  const headers = {
    "Content-Type": "application/json",
    ...(options.headers || {})
  };

  const token = getToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(path, {
    ...options,
    headers
  });

  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.message || "Request failed.");
  }

  return data;
}

function renderState(state) {
  elements.welcomeText.textContent = `${state.currentUser}'s chat room`;

  elements.onlineUsers.innerHTML = state.onlineUsers.length
    ? state.onlineUsers.map((username) => `
        <div class="list-item">
          <strong>${escapeHtml(username)}</strong>
          <span>${username === state.currentUser ? "You" : "Online"}</span>
        </div>
      `).join("")
    : '<p class="meta">Nobody is online right now.</p>';

  elements.leaderboard.innerHTML = state.leaderboard.length
    ? state.leaderboard.map((user, index) => `
        <div class="list-item">
          <strong>#${index + 1} ${escapeHtml(user.username)}</strong>
          <span>${user.points} pts</span>
        </div>
      `).join("")
    : '<p class="meta">Leaderboard is empty.</p>';

  elements.chatMessages.innerHTML = state.messages.length
    ? state.messages.map((message) => `
        <article class="message ${message.username === state.currentUser ? "own" : ""}">
          <div class="message-header">
            <strong>${escapeHtml(message.username)}</strong>
            <span class="meta">${formatTime(message.createdAtUtc)}</span>
          </div>
          <p>${escapeHtml(message.text)}</p>
        </article>
      `).join("")
    : '<p class="meta">No messages yet. Start the conversation.</p>';

  elements.chatMessages.scrollTop = elements.chatMessages.scrollHeight;
}

function showChatView() {
  elements.authPanel.classList.add("hidden");
  elements.chatPanel.classList.remove("hidden");
}

function showAuthView() {
  elements.chatPanel.classList.add("hidden");
  elements.authPanel.classList.remove("hidden");
  switchTab("login");
}

function startPolling() {
  stopPolling();
  pollHandle = window.setInterval(loadState, POLL_MS);
}

function stopPolling() {
  if (pollHandle) {
    clearInterval(pollHandle);
    pollHandle = null;
  }
}

async function loadState() {
  if (!getToken()) {
    showAuthView();
    return;
  }

  try {
    const state = await api("/api/chat/state", { method: "GET" });
    showChatView();
    renderState(state);
  } catch (error) {
    setToken(null);
    stopPolling();
    showAuthView();
    setStatus("Your session expired. Please log in again.", "error");
  }
}

async function signupUser(event) {
  event.preventDefault();

  try {
    const result = await api("/api/auth/signup", {
      method: "POST",
      body: JSON.stringify({
        email: elements.signupEmail.value.trim(),
        username: elements.signupUsername.value.trim(),
        password: elements.signupPassword.value
      })
    });

    setToken(result.token);
    elements.signupForm.reset();
    setStatus(result.message, "success");
    await loadState();
    startPolling();
  } catch (error) {
    setStatus(error.message, "error");
  }
}

async function loginUser(event) {
  event.preventDefault();

  try {
    const result = await api("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({
        username: elements.loginUsername.value.trim(),
        password: elements.loginPassword.value
      })
    });

    setToken(result.token);
    elements.loginForm.reset();
    setStatus(result.message, "success");
    await loadState();
    startPolling();
  } catch (error) {
    setStatus(error.message, "error");
  }
}

async function sendMessage(event) {
  event.preventDefault();
  const text = elements.messageInput.value.trim();
  if (!text) {
    return;
  }

  try {
    await api("/api/chat/messages", {
      method: "POST",
      body: JSON.stringify({ text })
    });

    elements.messageForm.reset();
    await loadState();
  } catch (error) {
    setStatus(error.message, "error");
  }
}

async function logoutUser() {
  try {
    await api("/api/auth/logout", { method: "POST" });
  } catch (error) {
  }

  setToken(null);
  stopPolling();
  showAuthView();
  setStatus("You have been logged out.", "success");
}

elements.tabButtons.forEach((button) => {
  button.addEventListener("click", () => switchTab(button.dataset.tab));
});

elements.signupForm.addEventListener("submit", signupUser);
elements.loginForm.addEventListener("submit", loginUser);
elements.messageForm.addEventListener("submit", sendMessage);
elements.logoutButton.addEventListener("click", logoutUser);

switchTab("signup");

if (getToken()) {
  loadState().then(startPolling);
}