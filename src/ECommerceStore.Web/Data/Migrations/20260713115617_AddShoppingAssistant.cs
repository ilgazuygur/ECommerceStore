using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceStore.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.CheckConstraint("CK_ChatConversations_LastSequence", "LastSequence >= 0");
                    table.ForeignKey(
                        name: "FK_ChatConversations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.CheckConstraint("CK_ChatMessages_Sequence", "Sequence > 0");
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessingAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssistantMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantRequests_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantRequests_ChatMessages_AssistantMessageId",
                        column: x => x.AssistantMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistantRequests_ChatMessages_UserMessageId",
                        column: x => x.UserMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageProducts",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayPosition = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PriceAtReplyTimeMinor = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageProducts", x => new { x.MessageId, x.DisplayPosition });
                    table.CheckConstraint("CK_ChatMessageProducts_DisplayPosition", "DisplayPosition > 0");
                    table.ForeignKey(
                        name: "FK_ChatMessageProducts_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessageProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantRequests_AssistantMessageId",
                table: "AssistantRequests",
                column: "AssistantMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantRequests_ConversationId_ClientRequestId",
                table: "AssistantRequests",
                columns: new[] { "ConversationId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantRequests_State_UpdatedAtUtc",
                table: "AssistantRequests",
                columns: new[] { "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantRequests_UserMessageId",
                table: "AssistantRequests",
                column: "UserMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_UserId_UpdatedAtUtc_Id",
                table: "ChatConversations",
                columns: new[] { "UserId", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageProducts_ProductId",
                table: "ChatMessageProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_Sequence",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantRequests");

            migrationBuilder.DropTable(
                name: "ChatMessageProducts");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");
        }
    }
}
