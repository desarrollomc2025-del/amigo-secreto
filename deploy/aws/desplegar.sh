#!/usr/bin/env bash
# Despliega la última versión de main en la EC2 usando AWS Systems Manager (SSM).
# No usa SSH: funciona desde cualquier red, solo requiere una sesión válida de AWS CLI (aws login).
#
# Uso (Git Bash):  deploy/aws/desplegar.sh
set -euo pipefail

INSTANCIA="${INSTANCIA:-i-00389e7baa8ca1def}"
REGION="${REGION:-us-east-2}"
AWS="${AWS:-aws}"
command -v "$AWS" >/dev/null 2>&1 || AWS="/c/Program Files/Amazon/AWSCLIV2/aws.exe"
export MSYS_NO_PATHCONV=1

# SSM ejecuta como root; el repo y Docker pertenecen a ec2-user.
SCRIPT='runuser -l ec2-user -c "cd ~/amigo-secreto && git pull -q && git log --oneline -1 && cd deploy/aws && docker compose up -d --build 2>&1 | tail -4 && sleep 15 && docker compose ps --format \"{{.Service}}: {{.Status}}\""'
PARAMS=$(printf '{"commands":[%s],"executionTimeout":["900"]}' "$(printf '%s' "$SCRIPT" | sed 's/\\/\\\\/g; s/"/\\"/g; s/.*/"&"/')")

echo "Enviando despliegue a $INSTANCIA..."
ID=$("$AWS" ssm send-command --region "$REGION" --instance-ids "$INSTANCIA" \
    --document-name AWS-RunShellScript --comment "Despliegue Amigo Secreto" \
    --parameters "$PARAMS" --query Command.CommandId --output text | tr -d '\r')

while :; do
    sleep 10
    ESTADO=$("$AWS" ssm get-command-invocation --region "$REGION" --command-id "$ID" --instance-id "$INSTANCIA" \
        --query Status --output text 2>/dev/null | tr -d '\r' || echo Pending)
    case "$ESTADO" in
        Pending|InProgress|Delayed) echo "  ...$ESTADO" ;;
        *) break ;;
    esac
done

"$AWS" ssm get-command-invocation --region "$REGION" --command-id "$ID" --instance-id "$INSTANCIA" \
    --query "[StandardOutputContent,StandardErrorContent]" --output text | tr -d '\r'
echo "Resultado: $ESTADO"
[ "$ESTADO" = Success ]
