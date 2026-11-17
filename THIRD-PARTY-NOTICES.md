# Avis de licences tiers — HisenseTvRemote

## pyvidaa 2.3.0 — portage C#

Le moteur natif (`src/HisenseTvRemote.Core/Vidaa/`) est une traduction en C#
de parties de pyvidaa (https://github.com/warrenrees/pyvidaa) : calcul des
identifiants MQTT dynamiques, sujets MQTT, déroulé de connexion et
d'appairage, stockage des jetons, découverte UPnP/SSDP, Wake-on-LAN.

```
MIT License

Copyright (c) 2024 Warren Rees

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## hisense_vidaa (stevene1919) — informations de protocole

Sujets MQTT et format du message de saisie de texte (`txtinputdata`,
`bwsinputdata`) repris de https://github.com/stevene1919/hisense_vidaa.
Licence MIT — Copyright (c) 2026 Steven Ebsary.

## MQTTnet 4.3.7 — dépendance NuGet

Bibliothèque client MQTT utilisée par le moteur natif.
Licence MIT — Copyright (c) .NET Foundation and Contributors.
https://github.com/dotnet/MQTTnet

## Certificat client VIDAA — non distribué

Le certificat client (`vidaa_client.pem`/`.key`) extrait de l'application
officielle VIDAA appartient à son propriétaire et n'est **pas** livré avec
cette application. Chaque poste doit fournir le sien
(`%USERPROFILE%\.config\pyvidaa\certs\`).
