const TOKEN_KEY = "privateChatHubToken";
const POLL_MS = 2000;

const elements = {
  authPanel: document.getElementById("authPanel"),
  chatPanel: document.getElementById("chatPanel"),
  authMessage: document.getElementById("authMessage"),
  chatNotice: document.getElementById("chatNotice"),
  signupForm: document.getElementById("signupForm"),
  loginForm: document.getElementById("loginForm"),
  signupEmail: document.getElementById("signupEmail"),
  signupUsername: document.getElementById("signupUsername"),
  signupPassword: document.getElementById("signupPassword"),
  loginUsername: document.getElementById("loginUsername"),
  loginPassword: document.getElementById("loginPassword"),
  welcomeText: document.getElementById("welcomeText"),
  roleBadge: document.getElementById("roleBadge"),
  onlineUsers: document.getElementById("onlineUsers"),
  leaderboard: document.getElementById("leaderboard"),
  adminPanel: document.getElementById("adminPanel"),
  clearChatButton: document.getElementById("clearChatButton"),
  chatMessages: document.getElementById("chatMessages"),
  messageForm: document.getElementById("messageForm"),
  messageInput: document.getElementById("messageInput"),
  embedInput: document.getElementById("embedInput"),
  fileInput: document.getElementById("fileInput"),
  fileName: document.getElementById("fileName"),
  replyBanner: document.getElementById("replyBanner"),
  replyUsername: document.getElementById("replyUsername"),
  replyPreview: document.getElementById("replyPreview"),
  cancelReplyButton: document.getElementById("cancelReplyButton"),
  logoutButton: document.getElementById("logoutButton"),
  tabButtons: document.querySelectorAll(".tab-button"),
  authForms: document.querySelectorAll(".auth-form")
};

let pollHandle = null;
let currentState = null;
let replyTarget = null;
let lastMessagesKey = "";
let lastSidebarKey = "";

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

function setNotice(target, message, type = "") {
  target.textContent = message;
  target.className = `status-message ${type}`.trim();
}

function clearNotice(target) {
  setNotice(target, "", "");
}

function setStatus(message, type = "") {
  setNotice(elements.authMessage, message, type);
}

function setChatNotice(message, type = "") {
  setNotice(elements.chatNotice, message, type);
}

function switchTab(tabName) {
  elements.tabButtons.forEach((button) => {
    button.classList.toggle("active", button.dataset.tab === tabName);
  });

  elements.authForms.forEach((form) => {
    form.classList.toggle("active", form.id === `${tabName}Form`);
  });

  clearNotice(elements.authMessage);
}

function escapeHtml(value) {
  const div = document.createElement("div");
  div.textContent = value ?? "";
  return div.innerHTML;
}

function formatTime(isoString) {
  return new Date(isoString).toLocaleString([], {
    dateStyle: "medium",
    timeStyle: "short"
  });
}

function formatSize(sizeBytes) {
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

async function api(path, options = {}) {
  const headers = { ...(options.headers || {}) };
  const token = getToken();

  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  if (!(options.body instanceof FormData) && !headers["Content-Type"]) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(path, { ...options, headers });
  const data = await response.json().catch(() => ({}));

  if (!response.ok) {
    const error = new Error(data.message || "Request failed.");
    error.status = response.status;
    throw error;
  }

  return data;
}

function showChatView() {
  elements.authPanel.classList.add("hidden");
  elements.chatPanel.classList.remove("hidden");
}

function showAuthView() {
  elements.chatPanel.classList.add("hidden");
  elements.authPanel.classList.remove("hidden");
  switchTab("login");
  clearNotice(elements.chatNotice);
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

function extractYouTubeId(url) {
  try {
    const parsed = new URL(url);
    if (parsed.hostname.includes("youtu.be")) {
      return parsed.pathname.slice(1) || null;
    }

    if (parsed.hostname.includes("youtube.com")) {
      return parsed.searchParams.get("v");
    }
  } catch (error) {
    return null;
  }

  return null;
}

function renderAttachment(attachment) {
  if (!attachment) {
    return "";
  }

  const safeUrl = escapeHtml(attachment.url);
  const safeName = escapeHtml(attachment.fileName);
  const type = attachment.contentType || "application/octet-stream";
  const imageMarkup = type.startsWith("image/")
    ? `<a href="${safeUrl}" target="_blank" rel="noreferrer"><img class="attachment-image" src="${safeUrl}" alt="${safeName}"></a>`
    : "";

  return `
    <div class="attachment">
      ${imageMarkup}
      <a class="attachment-link" href="${safeUrl}" target="_blank" rel="noreferrer">${safeName}</a>
      <span class="meta">${escapeHtml(type)}   ${escapeHtml(formatSize(attachment.sizeBytes))}</span>
    </div>
  `;
}

function renderEmbed(url) {
  if (!url) {
    return "";
  }

  const safeUrl = escapeHtml(url);
  const youtubeId = extractYouTubeId(url);
  if (youtubeId) {
    return `
      <div class="embed-card">
        <iframe class="embed-frame" src="https://www.youtube.com/embed/${escapeHtml(youtubeId)}" allowfullscreen loading="lazy"></iframe>
        <a class="embed-link" href="${safeUrl}" target="_blank" rel="noreferrer">Open embed link</a>
      </div>
    `;
  }

  if (/\.(png|jpe?g|gif|webp|bmp|svg)$/i.test(url)) {
    return `
      <div class="embed-card">
        <img class="embed-media" src="${safeUrl}" alt="Embedded media">
        <a class="embed-link" href="${safeUrl}" target="_blank" rel="noreferrer">Open image link</a>
      </div>
    `;
  }

  if (/\.(mp4|webm|ogg)$/i.test(url)) {
    return `
      <div class="embed-card">
        <video class="embed-video" src="${safeUrl}" controls preload="metadata"></video>
        <a class="embed-link" href="${safeUrl}" target="_blank" rel="noreferrer">Open video link</a>
      </div>
    `;
  }

  return `
    <div class="embed-card">
      <strong>Embedded link</strong>
      <a class="embed-link" href="${safeUrl}" target="_blank" rel="noreferrer">${safeUrl}</a>
    </div>
  `;
}

function renderReplySnippet(reply) {
  if (!reply) {
    return "";
  }

  const preview = reply.text || reply.attachmentFileName || "Original message";
  return `
    <div class="reply-snippet">
      <strong>Reply to ${escapeHtml(reply.username)}</strong>
      <p class="meta">${escapeHtml(preview)}</p>
    </div>
  `;
}

function renderBadges(user) {
  const badges = [];
  if (user.isBanned) badges.push('<span class="user-badge danger">Banned</span>');
  if (user.isMuted) badges.push('<span class="user-badge">Muted</span>');
  return badges.length ? `<div class="badge-row">${badges.join("")}</div>` : "";
}

function renderMessageActions(message) {
  const stateUser = currentState?.currentUser;
  if (!stateUser) {
    return "";
  }

  const actions = [
    `<button class="mini-button" data-action="reply" data-message-id="${message.id}">Reply</button>`
  ];

  if (message.canDelete) {
    actions.push(`<button class="mini-button" data-action="delete" data-message-id="${message.id}">Delete</button>`);
  }

  if (stateUser.isAdmin && message.username.toLowerCase() !== stateUser.username.toLowerCase()) {
    actions.push(`<button class="mod-button" data-action="kick" data-username="${escapeHtml(message.username)}">Kick</button>`);
    actions.push(`<button class="mod-button" data-action="mute" data-username="${escapeHtml(message.username)}">Mute</button>`);
    actions.push(`<button class="mod-button" data-action="timeout" data-username="${escapeHtml(message.username)}">Timeout</button>`);
    actions.push(`<button class="mod-button danger" data-action="ban" data-username="${escapeHtml(message.username)}">Ban</button>`);
    actions.push(`<button class="mod-button danger" data-action="ipban" data-username="${escapeHtml(message.username)}">IP Ban</button>`);
  }

  return `<div class="message-actions">${actions.join("")}</div>`;
}

function renderState(state) {
  const previousState = currentState;
  currentState = state;
  const sidebarKey = JSON.stringify({
    currentUser: state.currentUser,
    onlineUsers: state.onlineUsers,
    leaderboard: state.leaderboard
  });
  const messagesKey = JSON.stringify(state.messages);
  const sidebarChanged = sidebarKey !== lastSidebarKey;
  const messagesChanged = messagesKey !== lastMessagesKey;

  showChatView();

  if (sidebarChanged) {
    elements.welcomeText.textContent = `${state.currentUser.username}'s room`;
    elements.roleBadge.classList.toggle("hidden", !state.currentUser.isAdmin);
    elements.adminPanel.classList.toggle("hidden", !state.currentUser.isAdmin);

    elements.onlineUsers.innerHTML = state.onlineUsers.length
      ? state.onlineUsers.map((username) => `
          <div class="list-item">
            <strong>${escapeHtml(username)}</strong>
            <span>${username === state.currentUser.username ? "You" : "Online"}</span>
          </div>
        `).join("")
      : '<p class="meta">Nobody is online right now.</p>';

    elements.leaderboard.innerHTML = state.leaderboard.length
      ? state.leaderboard.map((user, index) => `
          <div class="list-item">
            <div>
              <strong>#${index + 1} ${escapeHtml(user.username)}</strong>
              ${renderBadges(user)}
            </div>
            <span>${user.points} pts</span>
          </div>
        `).join("")
      : '<p class="meta">Leaderboard is empty.</p>';

    lastSidebarKey = sidebarKey;
  }

  if (state.currentUser.isMuted) {
    setChatNotice(`You are muted until ${formatTime(state.currentUser.mutedUntilUtc)}.`, "error");
  } else if (!elements.chatNotice.dataset.locked) {
    clearNotice(elements.chatNotice);
  }

  if (messagesChanged) {
    const wasNearBottom = !previousState || (
      elements.chatMessages.scrollHeight - elements.chatMessages.scrollTop - elements.chatMessages.clientHeight < 120
    );

    elements.chatMessages.innerHTML = state.messages.length
      ? state.messages.map((message) => `
          <article class="message ${message.username === state.currentUser.username ? "own" : ""}">
            <div class="message-header">
              <strong>${escapeHtml(message.username)}</strong>
              <span class="meta">${formatTime(message.createdAtUtc)}</span>
            </div>
            ${renderReplySnippet(message.reply)}
            ${message.text ? `<p>${escapeHtml(message.text)}</p>` : ""}
            ${renderEmbed(message.embedUrl)}
            ${renderAttachment(message.attachment)}
            ${renderMessageActions(message)}
          </article>
        `).join("")
      : '<p class="meta">No messages yet. Start the conversation.</p>';

    if (wasNearBottom) {
      elements.chatMessages.scrollTop = elements.chatMessages.scrollHeight;
    }

    lastMessagesKey = messagesKey;
  }

  if (replyTarget && !state.messages.some((message) => message.id === replyTarget.id)) {
    clearReply();
  }

  elements.messageInput.disabled = state.currentUser.isMuted;
  elements.embedInput.disabled = state.currentUser.isMuted;
  elements.fileInput.disabled = state.currentUser.isMuted;
}

function setReply(messageId) {
  const message = currentState?.messages.find((entry) => entry.id === messageId);
  if (!message) {
    return;
  }

  replyTarget = {
    id: message.id,
    username: message.username,
    preview: message.text || message.attachment?.fileName || message.embedUrl || "Original message"
  };

  elements.replyUsername.textContent = replyTarget.username;
  elements.replyPreview.textContent = replyTarget.preview;
  elements.replyBanner.classList.remove("hidden");
  elements.messageInput.focus();
}

function clearReply() {
  replyTarget = null;
  elements.replyBanner.classList.add("hidden");
  elements.replyUsername.textContent = "";
  elements.replyPreview.textContent = "";
}

async function loadState() {
  if (!getToken()) {
    showAuthView();
    return;
  }

  try {
    const state = await api("/api/chat/state", { method: "GET" });
    if (elements.chatNotice.dataset.locked === "network") {
      delete elements.chatNotice.dataset.locked;
      clearNotice(elements.chatNotice);
    }
    renderState(state);
  } catch (error) {
    if (error.status === 401) {
      setToken(null);
      stopPolling();
      showAuthView();
      setStatus("Your session expired. Please log in again.", "error");
      return;
    }

    elements.chatNotice.dataset.locked = "network";
    setChatNotice("Connection hiccup. Retrying automatically...", "error");
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
  const embedUrl = elements.embedInput.value.trim();
  const file = elements.fileInput.files[0];

  if (!text && !embedUrl && !file) {
    return;
  }

  try {
    const formData = new FormData();
    formData.append("text", text);
    formData.append("embedUrl", embedUrl);
    if (replyTarget) {
      formData.append("replyToMessageId", replyTarget.id);
    }
    if (file) {
      formData.append("file", file);
    }

    const result = await api("/api/chat/messages", {
      method: "POST",
      body: formData
    });

    elements.messageForm.reset();
    elements.fileName.textContent = "No file selected";
    clearReply();
    setChatNotice(result.message, "success");
    delete elements.chatNotice.dataset.locked;
    await loadState();
  } catch (error) {
    elements.chatNotice.dataset.locked = "action";
    setChatNotice(error.message, "error");
  }
}

async function deleteMessage(messageId) {
  try {
    const result = await api(`/api/chat/messages/${messageId}`, { method: "DELETE" });
    setChatNotice(result.message, "success");
    delete elements.chatNotice.dataset.locked;
    await loadState();
  } catch (error) {
    elements.chatNotice.dataset.locked = "action";
    setChatNotice(error.message, "error");
  }
}

async function runAdminAction(action, targetUsername = null, durationMinutes = null) {
  try {
    const result = await api("/api/admin/actions", {
      method: "POST",
      body: JSON.stringify({ action, targetUsername, durationMinutes })
    });

    setChatNotice(result.message, "success");
    delete elements.chatNotice.dataset.locked;
    await loadState();
  } catch (error) {
    elements.chatNotice.dataset.locked = "action";
    setChatNotice(error.message, "error");
  }
}

async function logoutUser() {
  try {
    await api("/api/auth/logout", { method: "POST" });
  } catch (error) {
  }

  setToken(null);
  stopPolling();
  clearReply();
  currentState = null;
  lastMessagesKey = "";
  lastSidebarKey = "";
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
elements.cancelReplyButton.addEventListener("click", clearReply);
elements.clearChatButton.addEventListener("click", async () => {
  if (window.confirm("Clear the whole chat for everyone?")) {
    await runAdminAction("clear");
  }
});
elements.fileInput.addEventListener("change", () => {
  elements.fileName.textContent = elements.fileInput.files[0]?.name || "No file selected";
});
elements.chatMessages.addEventListener("click", async (event) => {
  const button = event.target.closest("button");
  if (!button) {
    return;
  }

  const action = button.dataset.action;
  const messageId = button.dataset.messageId;
  const username = button.dataset.username;

  if (action === "reply" && messageId) {
    setReply(messageId);
    return;
  }

  if (action === "delete" && messageId) {
    if (window.confirm("Delete this message?")) {
      await deleteMessage(messageId);
    }
    return;
  }

  if (!username) {
    return;
  }

  if (action === "timeout") {
    const value = window.prompt(`Timeout ${username} for how many minutes?`, "10");
    if (!value) {
      return;
    }

    const minutes = Number.parseInt(value, 10);
    if (!Number.isFinite(minutes) || minutes <= 0) {
      setChatNotice("Enter a valid number of minutes.", "error");
      return;
    }

    await runAdminAction("timeout", username, minutes);
    return;
  }

  const requiresConfirm = ["kick", "mute", "ban", "ipban"].includes(action);
  if (requiresConfirm && !window.confirm(`${action.toUpperCase()} ${username}?`)) {
    return;
  }

  await runAdminAction(action, username);
});

switchTab("signup");

if (getToken()) {
  loadState().then(startPolling);
}

