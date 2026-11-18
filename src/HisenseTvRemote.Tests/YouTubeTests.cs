using HisenseTvRemote.Core.YouTube;

namespace HisenseTvRemote.Tests;

/// <summary>« Envoyer un lien YouTube » : liens reconnus et réponses de la TV (DIAL) et de YouTube (Lounge).</summary>
public class YouTubeTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/watch?app=desktop&v=aqz-KE-bpKQ&t=42s", "aqz-KE-bpKQ")]
    [InlineData("youtube.com/watch?v=aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://m.youtube.com/watch?v=aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://music.youtube.com/watch?v=aqz-KE-bpKQ&list=RD123", "aqz-KE-bpKQ")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?si=abcdef", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/live/aqz-KE-bpKQ?feature=share", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("  aqz-KE-bpKQ  ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/@BlenderStudio", null)]                    // chaîne, pas une vidéo
    [InlineData("https://www.youtube.com/playlist?list=PL123", null)]
    [InlineData("https://vimeo.com/76979871", null)]
    [InlineData("https://evil.example/watch?v=aqz-KE-bpKQ", null)]                   // autre site
    [InlineData("https://youtube.com.evil.example/watch?v=aqz-KE-bpKQ", null)]
    [InlineData("pas un lien", null)]
    [InlineData("", null)]
    public void Liens_YouTube(string input, string? expected) => Assert.Equal(expected, YouTubeCaster.ParseVideoId(input));

    [Fact]
    public void ScreenId_de_la_reponse_DIAL_de_la_TV()
    {
        // Réponse réelle de la 55A53FXVT (18/11), identifiants raccourcis.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <service xmlns="urn:dial-multiscreen-org:schemas:dial" dialVer="2.2">
              <name>YouTube</name><options allowStop="true"/><state>running</state><link rel="run" href="run"/>
              <additionalData><deviceId>657c244c</deviceId><theme>cl</theme><screenId>e574d13e9175</screenId></additionalData>
            </service>
            """;
        Assert.Equal("e574d13e9175", YouTubeCaster.ParseScreenId(xml));
        Assert.Null(YouTubeCaster.ParseScreenId("<service><state>stopped</state></service>"));
    }

    [Fact]
    public void Commandes_numerotees_dans_la_session()
    {
        // Commande : deux tirets bas ; paramètres : un seul (sinon écran noir) ; numéro de la demande.
        var first = YouTubeCaster.FormatCommand(0, "setPlaylist", new Dictionary<string, string> { ["videoId"] = "aqz-KE-bpKQ" });
        Assert.Equal(new Dictionary<string, string> { ["count"] = "1", ["req0__sc"] = "setPlaylist", ["req0_videoId"] = "aqz-KE-bpKQ" }, first);
        var third = YouTubeCaster.FormatCommand(2, "addVideo", new Dictionary<string, string> { ["videoId"] = "eRsGyueVLvQ" });
        Assert.Equal("addVideo", third["req2__sc"]);
        Assert.Equal("eRsGyueVLvQ", third["req2_videoId"]);
    }

    [Fact]
    public void Jeton_et_session_YouTube()
    {
        Assert.Equal("AGdO5p8", YouTubeCaster.ParseLoungeToken("""{"screens":[{"screenId":"e574","loungeToken":"AGdO5p8","expiration":1763600000000}]}"""));
        Assert.Null(YouTubeCaster.ParseLoungeToken("""{"screens":[]}"""));
        Assert.Null(YouTubeCaster.ParseLoungeToken("pas du json"));

        const string bind = """
            183
            [[0,["c","8D5A1F2E3C4B","",8]
            ],[1,["S","HJ3kq1a9xQ"]
            ],[2,["loungeStatus",{"devices":"[]"}]]]
            """;
        Assert.Equal(("8D5A1F2E3C4B", "HJ3kq1a9xQ"), YouTubeCaster.ParseBind(bind));
        Assert.Null(YouTubeCaster.ParseBind("[[0,[\"noop\"]]]"));
    }
}
