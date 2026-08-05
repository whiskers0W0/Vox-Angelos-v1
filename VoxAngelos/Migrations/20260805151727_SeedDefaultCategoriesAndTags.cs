using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultCategoriesAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Categories: the same category->department mapping originally backfilled in
            // AddCategoriesToLguAccounts, reapplied here because it was found empty on the
            // live database (cleared at some point after that migration ran). Tags: seeded
            // from ConcernClassificationService.DefaultKeywordsByDepartment (capped at 30,
            // matching OfficeManagementModel.ParseTags's own limit) as a starting point staff
            // can edit — not previously populated for any department.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['infrastructure'],
                    "Tags" = ARRAY['road','roads','street','streets','highway','bridge','bridges','overpass','underpass','drainage','drain','canal','culvert','sewer','sewage','flood drain','storm drain','waterway','building','structure','public building','government building','construction','reconstruct','construct','maintenance','repair','dilapidated','deteriorating','damaged']
                WHERE "Department" = 'CEO';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['urban_planning', 'business_permits'],
                    "Tags" = ARRAY['urban planning','city planning','land use','land use plan','development plan','master plan','comprehensive plan','zoning','rezoning','zone','land conversion','permit','permits','building permit','business permit','economic development','economic growth','investment','city development','strategic planning','policy','subdivision','commercial','land','property','urban development','acdo','city development office','development office','city planner']
                WHERE "Department" = 'ACDO';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['environment', 'sanitation', 'waste_management'],
                    "Tags" = ARRAY['environment','environmental','waste','garbage','trash','rubbish','litter','littering','waste management','waste disposal','illegal dumping','dump site','recycling','recycle','composting','pollution','air pollution','water pollution','noise pollution','smoke','smokebelching','smoke belching','sanitation','cleanliness','unsanitary','stagnant water','standing water','mosquito','dengue','rats']
                WHERE "Department" = 'CENRO';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['public_safety', 'traffic'],
                    "Tags" = ARRAY['traffic','traffic flow','traffic congestion','traffic jam','traffic signal','traffic light','traffic sign','road sign','signage','road safety','parking','no parking','illegal parking','parking violation','transportation','public transportation','public transport','vehicle','vehicles','motor vehicle','enforcement','traffic enforcer','traffic officer','congestion','bottleneck','accident','road accident','vehicular accident','collision','speeding']
                WHERE "Department" = 'PTRO';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['pwd_affairs'],
                    "Tags" = ARRAY['disability','disabled','person with disability','persons with disability','pwd','pwd id','disability card','disability benefits','wheelchair','wheelchair ramp','ramp','ramps','accessibility','accessible','barrier','barrier free','inclusion','inclusive','special needs','blind','visually impaired','vision impaired','deaf','hearing impaired','mute','amputee','cerebral palsy','autism','autistic','mental health']
                WHERE "Department" = 'PWDAO';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['senior_citizen'],
                    "Tags" = ARRAY['senior','senior citizen','senior citizens','elderly','old age','aged','aging','retirement','retired','pensioner','osca','senior id','osca id','senior card','benefits for senior','senior discount','senior privileges','geriatric','nursing home','care home','assisted living','lolo','lola','grandparent','grandparents','manong','manang','elderly care','matanda','matatanda']
                WHERE "Department" = 'OSCA';

                UPDATE "AspNetUsers" SET
                    "Categories" = ARRAY['social_services'],
                    "Tags" = ARRAY['welfare','social welfare','social service','social services','financial aid','financial assistance','disaster relief','relief goods','relief','calamity','evacuation','evacuee','shelter','vulnerable','indigent','poor','poverty','children','child','minors','minor','orphan','orphans','women','woman','single mother','abused','homeless','squatter','informal settler']
                WHERE "Department" = 'SWDO';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "Categories" = ARRAY[]::text[], "Tags" = ARRAY[]::text[]
                WHERE "Department" IN ('CEO', 'ACDO', 'CENRO', 'PTRO', 'PWDAO', 'OSCA', 'SWDO');
                """);
        }
    }
}
