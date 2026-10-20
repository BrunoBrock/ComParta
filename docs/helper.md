# Run your own helper

A helper is a small program on a computer everyone can reach. When your PC is behind a home router,
or a phone is on mobile data, nobody can connect to it directly. With a helper, ComParta hands the
helper each file you share, and the helper passes it on to anyone who opens your link, in ComParta
or in any torrent app (qBittorrent, LibreTorrent, Flud). Nobody changes a router. The design is AD-23
in the architecture; how the program itself works is in
[src/ComParta.Helper/README.md](../src/ComParta.Helper/README.md).

The helper keeps a copy of every file it passes on, while it is shared. Run it on a computer you
control, and only give its key to people you trust.

There are two ways to run one:

- [On the free Oracle Cloud machine](#on-the-free-oracle-cloud-machine), for real use. It is always
  on, and the free tier includes about 10 TB a month of outgoing traffic.
- [On a spare Windows PC](#on-a-spare-windows-pc), for trying it out, when you can forward ports on
  that PC's router.

## Get the program

Each helper release in [GitHub Releases](../../../releases) (tags named `helper-v…`) has a
ready-to-run build that needs nothing else installed:

| File | For |
|---|---|
| `comparta-helper-linux-arm64.tar.gz` | The free Oracle Cloud machine (Ampere, Arm) |
| `comparta-helper-linux-x64.tar.gz` | Other Linux machines |
| `comparta-helper-win-x64.zip` | Windows |

The *Helper release* workflow makes these; run it by hand from the Actions tab to get the builds of
any commit as downloads of that run. To build one yourself with the .NET 10 SDK:

```
dotnet publish src/ComParta.Helper -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true -o out
```

## On the free Oracle Cloud machine

### 1. Create the machine

1. Sign up at [Oracle Cloud Free Tier](https://www.oracle.com/cloud/free/). The *home region* you
   choose is where the free machine lives; pick the one closest to the people you share with
   (for Brazil, *Brazil East (São Paulo)*). It cannot be changed later.
2. Create a compute instance: image **Ubuntu 24.04**, shape **VM.Standard.A1.Flex** (Ampere, marked
   *Always Free*), 1 OCPU and 6 GB of memory is plenty. Keep *Assign a public IPv4 address* on, and
   save the SSH key it offers.
   If it says there is no capacity, try another availability domain or try again later.
3. Note the instance's **public IP address**. Everything below calls it `203.0.113.7`; use yours.

### 2. Open the ports

The helper uses four ports. Oracle blocks them twice: in the cloud network and on the machine.

| Port | Protocol | What |
|---|---|---|
| 6881 | TCP | Torrent apps download the files here. |
| 6969 | TCP and UDP | The helper's public server, which tells apps where the files are. |
| 6970 | TCP | ComParta adds and removes files here, with the key. |
| 80 | TCP | Only for getting and renewing the certificate (step 4). |

In the Oracle console, open the instance's **Virtual cloud network**, then **Security Lists**, the
default list, and **Add Ingress Rules**: source `0.0.0.0/0`, protocol TCP, destination ports
`80,6881,6969,6970`; then one more rule with protocol UDP and destination port `6969`.

Then on the machine (`ssh ubuntu@203.0.113.7`), Oracle's Ubuntu image refuses everything but SSH
until told otherwise:

```
sudo iptables -I INPUT -p tcp -m multiport --dports 80,6881,6969,6970 -j ACCEPT
sudo iptables -I INPUT -p udp --dport 6969 -j ACCEPT
sudo netfilter-persistent save
```

### 3. Install the helper

```
sudo useradd --system --no-create-home comparta-helper
curl -LO <the link to comparta-helper-linux-arm64.tar.gz on the release page>
sudo tar -xzf comparta-helper-linux-arm64.tar.gz -C /opt
sudo cp /opt/comparta-helper/comparta-helper.service /etc/systemd/system/
sudo mkdir -p /etc/comparta-helper
```

Make the key, a long random secret. Keep the output; ComParta needs it in step 5.

```
openssl rand -hex 24
```

Write the settings, with your address and that key:

```
sudo tee /etc/comparta-helper/helper.env > /dev/null <<'EOF'
Helper__PublicAddress=203.0.113.7
Helper__Key=<the key from openssl>
EOF
sudo chmod 600 /etc/comparta-helper/helper.env
```

Other settings, such as how much space files may take (`Helper__MaxTotalBytes`, 20 GB unless set),
go in the same file; the README lists them all. Files are kept in `/var/lib/comparta-helper`.

### 4. Give it a certificate

Without a certificate the key travels in plain text, so the helper needs a name and a certificate
before real use.

1. Get a free name that points to your address, for example at [DuckDNS](https://www.duckdns.org):
   make `myhelper.duckdns.org` and set its address to `203.0.113.7`.
2. Get a certificate from Let's Encrypt:

   ```
   sudo apt install -y certbot
   sudo certbot certonly --standalone -d myhelper.duckdns.org
   ```

3. Let certbot hand each new certificate to the helper, which renewals then repeat on their own:

   ```
   sudo tee /etc/letsencrypt/renewal-hooks/deploy/comparta-helper.sh > /dev/null <<'EOF'
   #!/bin/sh
   install -o comparta-helper -m 644 /etc/letsencrypt/live/myhelper.duckdns.org/fullchain.pem /etc/comparta-helper/
   install -o comparta-helper -m 600 /etc/letsencrypt/live/myhelper.duckdns.org/privkey.pem /etc/comparta-helper/
   systemctl try-restart comparta-helper
   EOF
   sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/comparta-helper.sh
   sudo /etc/letsencrypt/renewal-hooks/deploy/comparta-helper.sh
   ```

4. Add the certificate to the settings:

   ```
   sudo tee -a /etc/comparta-helper/helper.env > /dev/null <<'EOF'
   Helper__CertificatePath=/etc/comparta-helper/fullchain.pem
   Helper__CertificateKeyPath=/etc/comparta-helper/privkey.pem
   EOF
   ```

### 5. Start it and use it

```
sudo systemctl enable --now comparta-helper
systemctl status comparta-helper
```

The status shows `ComParta helper running.` with its public server and address. If it names a
problem instead, fix that line in `/etc/comparta-helper/helper.env` and run
`sudo systemctl restart comparta-helper`. `journalctl -u comparta-helper` shows everything it said.

In ComParta, open **Settings**, then **Helper computer**:

- **Address**: `https://myhelper.duckdns.org:6970`
- **Key**: the key from step 3

Choose **Save helper**. Within a few seconds the connection indicator says **Others get your files
through your helper**, and every link you copy from then on reaches people through it.

## On a spare Windows PC

For trying the helper out. The PC's router must forward the ports, so this does not help a PC that
cannot be reached either.

1. Unzip `comparta-helper-win-x64.zip`.
2. On the router, forward TCP 6881, 6969 and 6970, and UDP 6969, to that PC. In Windows Firewall,
   allow `comparta-helper.exe` on public and private networks.
3. In `helper.json`, set `PublicAddress` to the address the internet sees for that router, and `Key`
   to a long random secret (at least 16 characters).
4. Run `comparta-helper.exe` and keep the window open.
5. In ComParta's **Settings**, **Helper computer**, enter `http://<that address>:6970` and the key.

This one speaks plain HTTP, so the key can be read by anyone on the networks in between. Give it a
certificate (`CertificatePath` and `CertificateKeyPath`, as PEM files) before sharing anything that
matters.

## Keeping it healthy

- **Updating**: download the new build, `sudo systemctl stop comparta-helper`, extract it over
  `/opt/comparta-helper` again, and start it. The files it holds stay.
- **Space**: the oldest file is dropped when a new one would not fit; a file is also dropped as soon
  as its share stops in ComParta.
- **A new key**: change `Helper__Key`, restart, and enter the new key in every ComParta that uses
  the helper. Old installs can no longer add anything.
- **A new address**: if the machine is ever recreated, update `Helper__PublicAddress` and the
  DuckDNS name; ComParta keeps working with the same name.
