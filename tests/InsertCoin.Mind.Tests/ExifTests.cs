using System.Text;

namespace InsertCoin.Mind.Tests;

public class ExifTests
{
    /// <summary>
    /// A JPEG's start with an EXIF block, as phones write one: the first directory holding the orientation and a
    /// pointer to the EXIF directory, which holds when it was taken.
    /// </summary>
    private static byte[] Photo(bool little, int orientation, string? taken)
    {
        var tiff = new List<byte>();
        void U16(int v) => tiff.AddRange(little ? [(byte)v, (byte)(v >> 8)] : [(byte)(v >> 8), (byte)v]);
        void U32(int v) => tiff.AddRange(little ? [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)] : [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
        tiff.AddRange(little ? "II"u8.ToArray() : "MM"u8.ToArray());
        U16(42);
        U32(8);                                             // the first directory, right after the header
        U16(2);                                             // two entries: the orientation, and where the EXIF directory is
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U16(0x8769); U16(4); U32(1); U32(8 + 2 + 2 * 12 + 4);
        U32(0);                                             // no next directory
        U16(1);                                             // the EXIF directory: when it was taken
        U16(0x9003); U16(2); U32(20); U32(8 + 2 + 2 * 12 + 4 + 2 + 12 + 4);
        U32(0);
        tiff.AddRange(Encoding.ASCII.GetBytes((taken ?? "0000:00:00 00:00:00") + "\0"));
        var app1 = new List<byte> { 0xFF, 0xE1 };
        var length = 2 + 6 + tiff.Count;
        app1.AddRange([(byte)(length >> 8), (byte)length]);
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);
        return [0xFF, 0xD8, .. app1, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_how_a_photo_is_turned_and_when_it_was_taken(bool little)
    {
        var (orientation, taken) = Exif.Read(Photo(little, 6, "2026:10:07 14:32:05"));

        Assert.Equal(6, orientation);
        Assert.Equal(new DateTime(2026, 10, 7, 14, 32, 5), taken);
    }

    [Fact]
    public void A_photo_without_exif_is_upright_and_undated()
    {
        Assert.Equal((1, (DateTime?)null), Exif.Read([0xFF, 0xD8, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9]));
        Assert.Equal((1, (DateTime?)null), Exif.Read("not a jpeg at all"u8));
    }

    [Fact]
    public void A_broken_date_or_a_cut_block_is_left_out_not_a_failure()
    {
        Assert.Null(Exif.Read(Photo(true, 1, "sometime last week x")).Taken);
        var cut = Photo(true, 3, "2026:10:07 14:32:05")[..30];
        Assert.Equal(1, Exif.Read(cut).Orientation);
    }

    [Fact]
    public async Task When_a_picture_was_taken_is_told_to_her_and_kept()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.FromHours(-4))));
        var model = new FakeModel("[saw: a lakeside path at sunset] Lovely.");
        var photo = new Attachment("walk.jpg", "image/jpeg", [1, 2, 3]) { Taken = new DateTime(2026, 10, 7, 18, 5, 0) };

        await conversation.Answer(model, "", "", _ => { }, (_, _) => { }, handed: [photo]);

        Assert.Contains("the picture \"walk.jpg\" (taken Wednesday 7 October 2026, 18:05)", model.Requests[0][^1].Text);
        Assert.Equal("[picture: walk.jpg, taken 2026-10-07 18:05: a lakeside path at sunset]", temp.Store.After("shino", 0)[0].Text);
    }
}
