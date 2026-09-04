set -euxo pipefail

dotnet build -v normal --nologo --disable-build-servers --no-restore --configuration Debug Gigaset
sudo ./Gigaset/bin/Debug/net8.0/gigaset --socket ./client.sock
