set -euxo pipefail

BUILD_CONFIGURATION="Release"
DOTNET_VERSION="net8.0"
TARGET_ARCH="linux-arm64"

PROJECT_PATH="Gigaset"
EXECUTABLE_FILENAME="gigaset"

REMOTE_HOST="rocky@dectpi.lan"
REMOTE_PATH="/home/rocky/rfpproxy"

dotnet build --nologo --configuration $BUILD_CONFIGURATION

rsync -a -P . $REMOTE_HOST:$REMOTE_PATH
ssh -t $REMOTE_HOST "sudo $REMOTE_PATH/$PROJECT_PATH/bin/$BUILD_CONFIGURATION/$DOTNET_VERSION/$TARGET_ARCH/$EXECUTABLE_FILENAME --socket $REMOTE_PATH/client.sock"
