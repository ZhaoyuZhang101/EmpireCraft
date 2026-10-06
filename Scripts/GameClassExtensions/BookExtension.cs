using System.Collections.Generic;

namespace EmpireCraft.Scripts.GameClassExtensions;

// 原版书籍的扩展数据：记住写书时作者属于哪个模组文化(文化藏书窗口按这个归类)；
// 名著还记住自己是哪部名著、出自哪个文化、已经启发过哪些文化——书被别的文化读到时，那个文化也受到启发。
public static class BookExtension
{
    public class BookExtraData : ExtraDataBase
    {
        public string landmark_id = "";
        // 写书时作者的模组文化
        public string culture = "";
        public string origin_culture = "";
        public List<string> inspired_cultures = new();
        // 理念著作(见 SpeechFreedomSystem)：宣扬的理念(PartyIdeology 名)；空 = 不是理念著作
        public string ideology = "";
    }

    // 不创建扩展，只看这本书是不是理念著作
    public static bool TryGetIdeologyTreatise(this Book book, out BookExtraData data)
    {
        data = book == null ? null : ExtensionManager<Book, BookExtraData>.GetOrCreate(book, true);
        return data != null && !string.IsNullOrEmpty(data.ideology);
    }

    public static BookExtraData GetOrCreate(this Book book, bool isSave = false) =>
        book.GetOrCreate<Book, BookExtraData>(isSave);

    // 不创建扩展，只看这本书是不是名著(读书补丁每次都会问，普通书不应该因此挂上空扩展)
    public static bool TryGetLandmark(this Book book, out BookExtraData data)
    {
        data = book == null ? null : ExtensionManager<Book, BookExtraData>.GetOrCreate(book, true);
        return data != null && !string.IsNullOrEmpty(data.landmark_id);
    }
}
