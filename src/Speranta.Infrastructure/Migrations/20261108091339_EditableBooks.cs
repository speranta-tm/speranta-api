using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Speranta.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditableBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "banner_file_id",
                table: "books",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "benefits",
                table: "books",
                type: "text[]",
                nullable: false,
                // Existing rows need a value before the seed below fills them in.
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "blurb",
                table: "books",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "closing",
                table: "books",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cover_file_id",
                table: "books",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "form_eyebrow",
                table: "books",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "form_title",
                table: "books",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "heading",
                table: "books",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lead",
                table: "books",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "paragraphs",
                table: "books",
                type: "text[]",
                nullable: false,
                // Existing rows need a value before the seed below fills them in.
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string> { "Poți descoperi tu singur adevărul Bibliei și sfaturile lui Dumnezeu pentru tine.", "Dacă nu ai încă o Biblie, ne bucurăm să ți-o oferim noi, fără niciun cost.", "Poți studia direct în liniștea casei tale." }, "Biblia sau Sfânta Scriptură — Cartea care a schimbat milioane de vieți.", "Dă-ți o șansă să-L cunoști pe Dumnezeu prin Cuvântul Său.", null, "Comandă o Biblie", "Primește o Biblie gratuit", "Biblia sau Sfânta Scriptură", "Cauți sens? Răspunsuri? Vrei să afli ce rezervă viitorul?", new List<string> { "S-ar putea ca răspunsul să fie mai aproape decât crezi — într-o Carte care a schimbat milioane de vieți. Îți oferim cadou o Biblie, complet gratuit.", "Dumnezeu are un mesaj personal pentru tine, iar fiecare pagină poate fi începutul unei noi călătorii." } });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string>(), "Ellen G. White despre Isus din Evanghelii, Mesia din profeții, Cel mort și înviat.", null, null, "Comandă cartea", "Primește gratuit „Viața lui Isus”", null, "Dacă ai ajuns pe această pagină, înseamnă că vrei să afli mai multe despre cine a fost Isus. Ne bucurăm să îți oferim această oportunitate!", new List<string> { "Despre Isus din Nazaret s-a spus: „Niciodată n-a vorbit vreun om ca omul acesta” (Ioan 7:46). Despre autoarea cărții Viața lui Isus s-ar putea spune: „Puțini au scris ca ea despre Omul acesta.”", "Citind această carte, Îl vei descoperi pe adevăratul Isus din Evanghelii, pe Mesia din profețiile Vechiului Testament, pe Cel mort și înviat. Pe Cel ce plânge alături de cei suferinzi și îi ține în brațe pe cei mici.", "Nu va fi doar o lectură, ci o întâlnire cu Acela care a spus: „Eu sunt Calea, Adevărul și Viața.”", "Autoarea cărții, Ellen G. White (1827 – 1915), a fost o persoană cu daruri spirituale remarcabile. A scris peste 40 de cărți și 5000 de articole pe teme diverse: religie, educație, relații sociale, profeție, sănătate și management. Opera sa este tradusă în multe limbi, iar în română sunt disponibile peste 50 de volume." } });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 3,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string>(), "Marea luptă dintre adevăr și minciună, de-a lungul istoriei lumii.", null, null, "Comandă cartea", "Primește gratuit „Tragedia veacurilor”", null, "Tragedia veacurilor se numără printre cele mai cunoscute și citite cărți ale scriitoarei creștine Ellen G. White, fiind a cincea dintr-o serie care prezintă istoria lumii din perspectiva Bibliei și a creștinismului.", new List<string> { "Volumul acesta abordează luptele din trecut pentru a scoate în evidență faptele și principiile care vor influența decisiv mersul lumii și evenimentele viitoare. În paginile sale sunt dezvăluite scenele marii lupte dintre adevăr și minciună, înșelăciunile lui Satana și mijloacele prin care i se poate rezista cu succes. Cititorii vor găsi o soluție satisfăcătoare pentru marea problemă a răului și a suferinței și vor descoperi dreptatea și iubirea lui Dumnezeu." } });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 4,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string>(), "Un ABC al vieții spirituale, pas cu pas spre Isus Hristos.", null, null, "Comandă cartea", "Primește gratuit „Calea către Hristos”", null, "Acest ABC al vieții spirituale, un mic tratat în domeniul devoțiunii, își enunță chiar din titlu misiunea pe care o are.", new List<string> { "Cartea îl îndrumă pe cititor spre Isus Hristos, ca fiind Singurul care poate să împlinească nevoile sufletului. Pașii celui care se simte nesigur sau cuprins de îndoială sunt conduși pe cărarea păcii, iar cel ce caută neprihănirea și desăvârșirea caracterului este îndrumat, pas cu pas, pe calea adevăratei viețuiri creștine.", "Cartea dezvăluie secretul biruinței și descoperă, într-o manieră simplă, harul mântuitor și puterea susținătoare a Marelui Prieten al întregii omeniri." } });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 5,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string> { "Poți descoperi povestirile Bibliei împreună cu copilul tău, sau le poate descoperi și el singur.", "Dacă nu ai această carte, suntem bucuroși să ți-o oferim complet gratuit.", "Copilul tău poate citi direct în liniștea casei." }, "Scriptura pe înțelesul copiilor: limbaj clar și ilustrații pline de culoare.", "Dă-i o șansă copilului tău să-L cunoască pe Dumnezeu prin Cuvântul Său.", null, "Comandă cartea", "Primește gratuit „Biblia pentru elevi”", null, "„Biblia pentru elevi” este genul de carte care face Scriptura accesibilă, prietenoasă și plină de culoare pentru copilul tău.", new List<string> { "Nu este doar o simplă Biblie, ci un ghid care îl ajută să înțeleagă mai ușor povestirile, personajele și ideile biblice. Folosește un limbaj clar, explicații scurte pe marginea paginii și ilustrații atractive, și este concepută în așa fel încât copilul tău să citească singur povestirile biblice fără să se plictisească.", "Dacă copilul tău e mai mic și încă nu știe să citească, i-o poți citi tu, descoperind împreună momentele minunate din Biblie, eroii credinței, oamenii lui Dumnezeu care și-au păstrat credința chiar și în momente grele." } });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 6,
                columns: new[] { "banner_file_id", "benefits", "blurb", "closing", "cover_file_id", "form_eyebrow", "form_title", "heading", "lead", "paragraphs" },
                values: new object[] { null, new List<string> { "Poți descoperi minunatele povestiri ale Bibliei împreună cu copilul tău.", "Dacă nu ai această carte, suntem bucuroși să ți-o oferim complet gratuit.", "Poți citi împreună cu copilul direct în liniștea casei tale." }, "Povestiri și versete biblice de citit împreună cu copilul.", "Dă-i o șansă copilului tău să-L cunoască pe Dumnezeu prin Cuvântul Său.", null, "Comandă cartea", "Primește gratuit „Mesajul lui Dumnezeu pentru tine”", null, "O colecție a celor mai importante povestiri și versete din Biblie, într-o traducere contemporană. Citindu-le la rând, capitol cu capitol, ajungi să cunoști Cuvântul lui Dumnezeu ca pe o povestire în desfășurare.", new List<string> { "Cel mai interesant este că și tu, ca părinte, ai un rol în această poveste. Cartea este concepută pentru a fi citită copiilor de către părinți. Fiecare capitol al acestei ediții frumos ilustrate conține texte luate direct din Biblie, astfel că poți citi Cuvântul lui Dumnezeu împreună cu copilul tău, așa cum a fost scris.", "Cartea are de asemenea scurte explicații ale evenimentelor culturale și istorice, ca să te ajute să urmărești acțiunea, și câteva teme de reflecție despre cum se poate aplica textul la viața noastră de astăzi." } });

            migrationBuilder.CreateIndex(
                name: "ix_books_banner_file_id",
                table: "books",
                column: "banner_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_books_cover_file_id",
                table: "books",
                column: "cover_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_books_files_banner_file_id",
                table: "books",
                column: "banner_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_books_files_cover_file_id",
                table: "books",
                column: "cover_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_books_files_banner_file_id",
                table: "books");

            migrationBuilder.DropForeignKey(
                name: "fk_books_files_cover_file_id",
                table: "books");

            migrationBuilder.DropIndex(
                name: "ix_books_banner_file_id",
                table: "books");

            migrationBuilder.DropIndex(
                name: "ix_books_cover_file_id",
                table: "books");

            migrationBuilder.DropColumn(
                name: "banner_file_id",
                table: "books");

            migrationBuilder.DropColumn(
                name: "benefits",
                table: "books");

            migrationBuilder.DropColumn(
                name: "blurb",
                table: "books");

            migrationBuilder.DropColumn(
                name: "closing",
                table: "books");

            migrationBuilder.DropColumn(
                name: "cover_file_id",
                table: "books");

            migrationBuilder.DropColumn(
                name: "form_eyebrow",
                table: "books");

            migrationBuilder.DropColumn(
                name: "form_title",
                table: "books");

            migrationBuilder.DropColumn(
                name: "heading",
                table: "books");

            migrationBuilder.DropColumn(
                name: "lead",
                table: "books");

            migrationBuilder.DropColumn(
                name: "paragraphs",
                table: "books");
        }
    }
}
