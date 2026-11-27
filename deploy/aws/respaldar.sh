#!/usr/bin/env bash
# Respaldo completo de producción descargado a esta computadora, por AWS Systems Manager (sin SSH ni S3):
#   - AmigoSecreto.bak  (BACKUP DATABASE de SQL Server)
#   - json/<tabla>.json (todas las tablas en JSON, legibles sin SQL Server)
#   - .env              (secretos de producción: SQL_PASSWORD, ADMIN_KEY, CORREO_*)
#   - llaves/           (llaves de Data Protection de la app)
# El paquete se transfiere en partes por la salida de SSM y se verifica con SHA-256.
#
# Uso (Git Bash, con sesión de AWS CLI válida — «aws login»):
#   deploy/aws/respaldar.sh
# Variables opcionales: INSTANCIA, REGION, DESTINO (carpeta local).
set -euo pipefail

INSTANCIA="${INSTANCIA:-i-00389e7baa8ca1def}"
REGION="${REGION:-us-east-2}"
DESTINO="${DESTINO:-/c/Aplicaciones/Respaldos/AmigoSecreto/$(date +%Y-%m-%d)/produccion}"
AWS="${AWS:-aws}"
command -v "$AWS" >/dev/null 2>&1 || AWS="/c/Program Files/Amazon/AWSCLIV2/aws.exe"
export MSYS_NO_PATHCONV=1
TROZO=20000 # SSM devuelve como máximo 24 000 caracteres de salida por comando

# Ejecuta un script bash (como root) en la instancia y devuelve su salida estándar.
remoto() {
    local b64 id estado
    b64=$(printf '%s' "$1" | base64 -w0)
    # El script va a un archivo y no por la entrada de bash: «docker compose exec» leería el resto del script.
    local cmd="f=\$(mktemp) && echo $b64 | base64 -d > \$f && bash \$f < /dev/null; r=\$?; rm -f \$f; exit \$r"
    id=$("$AWS" ssm send-command --region "$REGION" --instance-ids "$INSTANCIA" \
        --document-name AWS-RunShellScript --comment "Respaldo Amigo Secreto" \
        --parameters "{\"commands\":[\"$cmd\"],\"executionTimeout\":[\"600\"]}" \
        --query Command.CommandId --output text | tr -d '\r')
    while :; do
        sleep 2
        estado=$("$AWS" ssm get-command-invocation --region "$REGION" --command-id "$id" --instance-id "$INSTANCIA" \
            --query Status --output text 2>/dev/null | tr -d '\r' || echo Pending)
        case "$estado" in Pending|InProgress|Delayed) ;; *) break ;; esac
    done
    if [ "$estado" != Success ]; then
        "$AWS" ssm get-command-invocation --region "$REGION" --command-id "$id" --instance-id "$INSTANCIA" \
            --query "[StandardOutputContent,StandardErrorContent]" --output text | tr -d '\r' >&2
        echo "Falló el comando remoto ($estado)." >&2
        return 1
    fi
    "$AWS" ssm get-command-invocation --region "$REGION" --command-id "$id" --instance-id "$INSTANCIA" \
        --query StandardOutputContent --output text | tr -d '\r'
}

PREPARAR=$(cat <<'REMOTO'
set -euo pipefail
cd /home/ec2-user/amigo-secreto/deploy/aws
R=/tmp/respaldo-amigo; rm -rf "$R" /tmp/respaldo-amigo.tgz; mkdir -p "$R/json" "$R/llaves"
sqlq() { docker compose exec -T sql sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d AmigoSecreto -b -h -1 -W -Q "$1"' _ "$1"; }
# JSON de una tabla como un solo valor (-y 0: sin cortar ni rellenar con espacios). Tabla vacía → [].
sqljson() { docker compose exec -T sql sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d AmigoSecreto -b -y 0 -Q "SET NOCOUNT ON; SELECT (SELECT * FROM [$1] FOR JSON PATH, INCLUDE_NULL_VALUES) AS j"' _ "$1" | grep '^\[' || echo '[]'; }
docker compose exec -T sql mkdir -p /var/opt/mssql/respaldo
sqlq "BACKUP DATABASE AmigoSecreto TO DISK='/var/opt/mssql/respaldo/AmigoSecreto.bak' WITH INIT, FORMAT, CHECKSUM" >/dev/null
docker compose cp sql:/var/opt/mssql/respaldo/AmigoSecreto.bak "$R/AmigoSecreto.bak" >/dev/null 2>&1
docker compose exec -T sql rm -f /var/opt/mssql/respaldo/AmigoSecreto.bak
for t in $(sqlq "SET NOCOUNT ON; SELECT name FROM sys.tables ORDER BY name" | tr -d ' \r'); do
    sqljson "$t" > "$R/json/$t.json"
done
cp .env "$R/.env"
docker compose cp app:/llaves/. "$R/llaves/" >/dev/null 2>&1 || true
runuser -u ec2-user -- git -C /home/ec2-user/amigo-secreto log --oneline -1 > "$R/version.txt"
docker compose ps --format '{{.Service}} {{.Image}} {{.Status}}' >> "$R/version.txt"
tar czf /tmp/respaldo-amigo.tgz -C "$R" .
rm -rf "$R"
echo "$(stat -c %s /tmp/respaldo-amigo.tgz) $(base64 -w0 /tmp/respaldo-amigo.tgz | wc -c) $(sha256sum /tmp/respaldo-amigo.tgz | cut -d' ' -f1)"
REMOTO
)

echo "Preparando el respaldo en $INSTANCIA..."
read -r BYTES LARGO SHA < <(remoto "$PREPARAR")
[ -n "${SHA:-}" ] || { echo "No se pudo preparar el respaldo en la instancia." >&2; exit 1; }
echo "  Paquete: $BYTES bytes (SHA-256 $SHA)"

mkdir -p "$DESTINO"
TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT
PARTES=$(( (LARGO + TROZO - 1) / TROZO ))
for ((i = 0; i < PARTES; i++)); do
    printf '  Descargando parte %d de %d\r' $((i + 1)) "$PARTES"
    remoto "base64 -w0 /tmp/respaldo-amigo.tgz | cut -c $((i * TROZO + 1))-$(((i + 1) * TROZO))" | tr -d '\n' >> "$TMP"
done
echo

base64 -d "$TMP" > "$DESTINO/respaldo.tgz"
LOCAL=$(sha256sum "$DESTINO/respaldo.tgz" | cut -d' ' -f1)
remoto "rm -f /tmp/respaldo-amigo.tgz" >/dev/null
if [ "$LOCAL" != "$SHA" ]; then
    echo "ERROR: el SHA-256 no coincide ($LOCAL). Vuelve a ejecutar el respaldo." >&2
    exit 1
fi
tar xzf "$DESTINO/respaldo.tgz" -C "$DESTINO"
echo "Respaldo verificado en $DESTINO:"
ls -la "$DESTINO" "$DESTINO/json"
