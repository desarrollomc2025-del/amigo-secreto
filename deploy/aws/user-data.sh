#!/bin/bash
# Arranque de la EC2 (Amazon Linux 2023): Docker + Compose + swap + código.
set -eux
dnf install -y docker git
systemctl enable --now docker
usermod -aG docker ec2-user
mkdir -p /usr/local/lib/docker/cli-plugins
curl -fsSL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m)" \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
curl -fsSL "https://github.com/docker/buildx/releases/download/v0.19.3/buildx-v0.19.3.linux-amd64" \
  -o /usr/local/lib/docker/cli-plugins/docker-buildx
chmod +x /usr/local/lib/docker/cli-plugins/docker-buildx
# Swap de 2 GB: la compilación .NET + SQL Server aprietan la memoria
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile swap swap defaults 0 0' >> /etc/fstab
sudo -u ec2-user git clone https://github.com/desarrollomc2025-del/amigo-secreto.git /home/ec2-user/amigo-secreto
touch /var/lib/cloud/instance/amigo-listo
