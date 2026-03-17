#!/bin/bash

# AgentBot Installation Script for Linux systemd
# This script is intended to be run AFTER cloning the repository.
# It will: verify prerequisites, publish the app, create a systemd service.
#
# Usage: sudo ./install.sh [--install-dir /opt/agentbot]
#
# Run as root or with sudo privileges.

set -euo pipefail

# ─── Colors ───────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# ─── Configuration ────────────────────────────────────────────────────
SERVICE_NAME="agentbot"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXECUTABLE_NAME="AgentBot"
NON_ROOT_USER="agentbot"
DOTNET_RUNTIME="linux-x64"
DEFAULT_INSTALL_DIR="/opt/agentbot"

# Parse optional --install-dir argument
INSTALL_DIR="${DEFAULT_INSTALL_DIR}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)
            INSTALL_DIR="$2"
            shift 2
            ;;
        *)
            echo -e "${RED}Unknown argument: $1${NC}"
            echo "Usage: sudo ./install.sh [--install-dir /path/to/dir]"
            exit 1
            ;;
    esac
done

# ─── Banner ───────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}╔══════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║     AgentBot Installation Script         ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════╝${NC}"
echo ""

# ─── Step 0: Root check ──────────────────────────────────────────────
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: This script must be run as root or with sudo.${NC}"
    echo "Usage: sudo ./install.sh"
    exit 1
fi

echo -e "${CYAN}Source directory:       ${REPO_DIR}${NC}"
echo -e "${CYAN}Installation directory: ${INSTALL_DIR}${NC}"
echo ""

# ─── Step 1: Verify prerequisites ────────────────────────────────────
echo -e "${YELLOW}[1/7] Checking prerequisites...${NC}"

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK is not installed.${NC}"
    echo ""
    echo "Install it with:"
    echo "  # Ubuntu/Debian:"
    echo "  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh"
    echo "  chmod +x dotnet-install.sh"
    echo "  ./dotnet-install.sh --channel 9.0"
    echo ""
    echo "  # Or via package manager:"
    echo "  sudo apt-get update && sudo apt-get install -y dotnet-sdk-9.0"
    exit 1
fi

# Check .NET SDK version (need 9.0+)
DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "0.0.0")
DOTNET_MAJOR=$(echo "${DOTNET_VERSION}" | cut -d'.' -f1)

if [ "${DOTNET_MAJOR}" -lt 9 ] 2>/dev/null; then
    echo -e "${RED}Error: .NET SDK 9.0 or later is required. Found: ${DOTNET_VERSION}${NC}"
    echo "Install .NET 9.0 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi
echo -e "${GREEN}  ✓ .NET SDK ${DOTNET_VERSION} found${NC}"

# Check for systemd
if ! command -v systemctl &> /dev/null; then
    echo -e "${RED}Error: systemd is not available on this system.${NC}"
    echo "This script requires systemd to manage the service."
    exit 1
fi
echo -e "${GREEN}  ✓ systemd available${NC}"

# Check that the project file exists in source directory
if [ ! -f "${REPO_DIR}/AgentBot.csproj" ]; then
    echo -e "${RED}Error: AgentBot.csproj not found in ${REPO_DIR}${NC}"
    echo "Make sure you are running this script from the cloned repository root."
    exit 1
fi
echo -e "${GREEN}  ✓ AgentBot.csproj found${NC}"
echo ""

# ─── Step 2: Publish the application ─────────────────────────────────
echo -e "${YELLOW}[2/7] Publishing the application...${NC}"

mkdir -p "${INSTALL_DIR}"

dotnet publish "${REPO_DIR}/AgentBot.csproj" \
    -c Release \
    -r "${DOTNET_RUNTIME}" \
    --self-contained true \
    -o "${INSTALL_DIR}" \
    --nologo

if [ ! -f "${INSTALL_DIR}/${EXECUTABLE_NAME}" ]; then
    echo -e "${RED}Error: Publish succeeded but executable '${EXECUTABLE_NAME}' not found in ${INSTALL_DIR}${NC}"
    echo "Check the publish output above for errors."
    exit 1
fi

chmod +x "${INSTALL_DIR}/${EXECUTABLE_NAME}"
echo -e "${GREEN}  ✓ Application published to ${INSTALL_DIR}${NC}"
echo ""

# ─── Step 3: Create service user and group ────────────────────────────
echo -e "${YELLOW}[3/7] Configuring service user...${NC}"

# Create group explicitly if it doesn't exist
if ! getent group "${NON_ROOT_USER}" &> /dev/null; then
    groupadd -r "${NON_ROOT_USER}"
    echo -e "${GREEN}  ✓ Group '${NON_ROOT_USER}' created${NC}"
else
    echo -e "${GREEN}  ✓ Group '${NON_ROOT_USER}' already exists${NC}"
fi

# Create user explicitly if it doesn't exist
if ! id -u "${NON_ROOT_USER}" &> /dev/null 2>&1; then
    useradd -r -g "${NON_ROOT_USER}" -s /bin/false "${NON_ROOT_USER}"
    if [ $? -ne 0 ]; then
        echo -e "${RED}Error: Failed to create user '${NON_ROOT_USER}'.${NC}"
        exit 1
    fi
    echo -e "${GREEN}  ✓ User '${NON_ROOT_USER}' created${NC}"
else
    echo -e "${GREEN}  ✓ User '${NON_ROOT_USER}' already exists${NC}"
fi
echo ""

# ─── Step 4: Set ownership and create directories ────────────────────
echo -e "${YELLOW}[4/7] Setting up directories and permissions...${NC}"

# Set ownership ONLY on the publish directory (not the source repo!)
chown -R "${NON_ROOT_USER}:${NON_ROOT_USER}" "${INSTALL_DIR}"
echo -e "${GREEN}  ✓ Ownership of ${INSTALL_DIR} set to '${NON_ROOT_USER}'${NC}"

# Create logs directory
LOGS_DIR="${INSTALL_DIR}/logs"
if [ ! -d "${LOGS_DIR}" ]; then
    mkdir -p "${LOGS_DIR}"
fi
chown -R "${NON_ROOT_USER}:${NON_ROOT_USER}" "${LOGS_DIR}"
echo -e "${GREEN}  ✓ Logs directory configured: ${LOGS_DIR}${NC}"

# Create data directory for SQLite databases
DATA_DIR="${INSTALL_DIR}/data"
if [ ! -d "${DATA_DIR}" ]; then
    mkdir -p "${DATA_DIR}"
fi
chown -R "${NON_ROOT_USER}:${NON_ROOT_USER}" "${DATA_DIR}"
echo -e "${GREEN}  ✓ Data directory configured: ${DATA_DIR}${NC}"
echo ""

# ─── Step 5: Handle existing service ─────────────────────────────────
if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
    echo -e "${YELLOW}[5/7] Service '${SERVICE_NAME}' is already running.${NC}"
    read -p "Do you want to reinstall? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Installation cancelled.${NC}"
        exit 0
    fi
    echo -e "${YELLOW}  Stopping existing service...${NC}"
    systemctl stop "${SERVICE_NAME}" || true
    systemctl disable "${SERVICE_NAME}" || true
    echo -e "${GREEN}  ✓ Existing service stopped${NC}"
else
    echo -e "${GREEN}[5/7] No existing service found — clean install${NC}"
fi
echo ""

# ─── Step 6: Create systemd service file ──────────────────────────────
echo -e "${YELLOW}[6/7] Creating systemd service file...${NC}"
cat > "${SERVICE_FILE}" << EOF
[Unit]
Description=AgentBot Telegram Bot with AI Agent
After=network.target

[Service]
Type=notify
User=${NON_ROOT_USER}
Group=${NON_ROOT_USER}
WorkingDirectory=${INSTALL_DIR}
ExecStart=${INSTALL_DIR}/${EXECUTABLE_NAME}
Restart=always
RestartSec=10

# Environment
Environment="DOTNET_ENVIRONMENT=Production"
EnvironmentFile=-${INSTALL_DIR}/.env

# Security hardening
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=false
PrivateTmp=true

# Allow write access to installation directory
ReadWritePaths=${INSTALL_DIR}

# Resource limits
LimitNOFILE=65536

# Watchdog: restart if notify is not received within 30s
WatchdogSec=30

[Install]
WantedBy=multi-user.target
EOF

echo -e "${GREEN}  ✓ Service file created at ${SERVICE_FILE}${NC}"

# Copy .env file if it exists in the repo but not yet in install dir
if [ -f "${REPO_DIR}/.env" ] && [ ! -f "${INSTALL_DIR}/.env" ]; then
    cp "${REPO_DIR}/.env" "${INSTALL_DIR}/.env"
    chown "${NON_ROOT_USER}:${NON_ROOT_USER}" "${INSTALL_DIR}/.env"
    chmod 600 "${INSTALL_DIR}/.env"
    echo -e "${GREEN}  ✓ .env file copied to ${INSTALL_DIR}${NC}"
fi

# Copy Config directory if it exists
if [ -d "${REPO_DIR}/Config" ] && [ ! -d "${INSTALL_DIR}/Config" ]; then
    cp -r "${REPO_DIR}/Config" "${INSTALL_DIR}/Config"
    chown -R "${NON_ROOT_USER}:${NON_ROOT_USER}" "${INSTALL_DIR}/Config"
    chmod 600 "${INSTALL_DIR}/Config/"*.json 2>/dev/null || true
    echo -e "${GREEN}  ✓ Config directory copied to ${INSTALL_DIR}${NC}"
fi
echo ""

# ─── Step 7: Enable and start service ────────────────────────────────
echo -e "${YELLOW}[7/7] Enabling and starting service...${NC}"

systemctl daemon-reload
echo -e "${GREEN}  ✓ Systemd daemon reloaded${NC}"

systemctl enable "${SERVICE_NAME}"
echo -e "${GREEN}  ✓ Service enabled (auto-start on boot)${NC}"

systemctl start "${SERVICE_NAME}"

# Wait for service to start (up to 15 seconds, not a fixed sleep)
STARTED=false
for i in $(seq 1 15); do
    if systemctl is-active --quiet "${SERVICE_NAME}"; then
        STARTED=true
        break
    fi
    sleep 1
done

echo ""
if [ "$STARTED" = true ]; then
    echo -e "${GREEN}╔══════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║      Installation Complete! ✓            ║${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════╝${NC}"
    echo ""
    echo "Service management commands:"
    echo -e "  ${CYAN}systemctl status ${SERVICE_NAME}${NC}     - Check service status"
    echo -e "  ${CYAN}systemctl stop ${SERVICE_NAME}${NC}       - Stop service"
    echo -e "  ${CYAN}systemctl restart ${SERVICE_NAME}${NC}    - Restart service"
    echo -e "  ${CYAN}systemctl disable ${SERVICE_NAME}${NC}    - Disable auto-start"
    echo -e "  ${CYAN}journalctl -u ${SERVICE_NAME} -f${NC}     - View logs (follow mode)"
    echo ""
    echo -e "Logs location:           ${CYAN}${LOGS_DIR}${NC}"
    echo -e "Installation directory:  ${CYAN}${INSTALL_DIR}${NC}"
    echo -e "Source directory:         ${CYAN}${REPO_DIR}${NC}"
else
    echo -e "${RED}╔══════════════════════════════════════════╗${NC}"
    echo -e "${RED}║      Service failed to start! ✗          ║${NC}"
    echo -e "${RED}╚══════════════════════════════════════════╝${NC}"
    echo ""
    echo "Check logs for details:"
    echo -e "  ${CYAN}journalctl -u ${SERVICE_NAME} -n 50 --no-pager${NC}"
    echo -e "  ${CYAN}cat ${LOGS_DIR}/app.log${NC}"
    exit 1
fi
