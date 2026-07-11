using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpecTen.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    OfficialDomain = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Trigger = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AddedModels = table.Column<int>(type: "integer", nullable: false),
                    UpdatedSpecs = table.Column<int>(type: "integer", nullable: false),
                    ReviewItemsCreated = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "phone_models",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BrandId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LaunchPriceUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ImageSourceUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phone_models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_phone_models_brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "benchmark_scores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: false),
                    BenchmarkName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_benchmark_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_benchmark_scores_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classification_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Basis = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classification_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_classification_snapshots_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "correction_reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReporterEmail = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_correction_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_correction_reports_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "phone_variants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RamGb = table.Column<int>(type: "integer", nullable: true),
                    StorageGb = table.Column<int>(type: "integer", nullable: true),
                    Color = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phone_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_phone_variants_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_items_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "source_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    BrandId = table.Column<int>(type: "integer", nullable: true),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RobotsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_documents_brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "brands",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_source_documents_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "spec_facts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spec_facts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spec_facts_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PhoneModelId = table.Column<int>(type: "integer", nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RawValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_claims_phone_models_PhoneModelId",
                        column: x => x.PhoneModelId,
                        principalTable: "phone_models",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_source_claims_source_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_benchmark_scores_PhoneModelId_BenchmarkName_SourceName",
                table: "benchmark_scores",
                columns: new[] { "PhoneModelId", "BenchmarkName", "SourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brands_Slug",
                table: "brands",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classification_snapshots_PhoneModelId_CreatedAt",
                table: "classification_snapshots",
                columns: new[] { "PhoneModelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_correction_reports_PhoneModelId",
                table: "correction_reports",
                column: "PhoneModelId");

            migrationBuilder.CreateIndex(
                name: "IX_correction_reports_Status",
                table: "correction_reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_import_runs_StartedAt",
                table: "import_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_phone_models_BrandId_Slug",
                table: "phone_models",
                columns: new[] { "BrandId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_phone_variants_PhoneModelId_Name",
                table: "phone_variants",
                columns: new[] { "PhoneModelId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_items_PhoneModelId",
                table: "review_items",
                column: "PhoneModelId");

            migrationBuilder.CreateIndex(
                name: "IX_review_items_Status",
                table: "review_items",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_source_claims_PhoneModelId_FieldKey",
                table: "source_claims",
                columns: new[] { "PhoneModelId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_source_claims_SourceDocumentId",
                table: "source_claims",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_BrandId",
                table: "source_documents",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_PhoneModelId",
                table: "source_documents",
                column: "PhoneModelId");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_SourceName_SourceUrl_ContentHash",
                table: "source_documents",
                columns: new[] { "SourceName", "SourceUrl", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_spec_facts_PhoneModelId_Key",
                table: "spec_facts",
                columns: new[] { "PhoneModelId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spec_facts_Status",
                table: "spec_facts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benchmark_scores");

            migrationBuilder.DropTable(
                name: "classification_snapshots");

            migrationBuilder.DropTable(
                name: "correction_reports");

            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropTable(
                name: "phone_variants");

            migrationBuilder.DropTable(
                name: "review_items");

            migrationBuilder.DropTable(
                name: "source_claims");

            migrationBuilder.DropTable(
                name: "spec_facts");

            migrationBuilder.DropTable(
                name: "source_documents");

            migrationBuilder.DropTable(
                name: "phone_models");

            migrationBuilder.DropTable(
                name: "brands");
        }
    }
}
