namespace AiEmailer.Api.Data
{
    public class DB
    {
        private readonly IConfiguration _config;
        public DB(IConfiguration config) { 
        _config = config;
        }

        public string GetConnectionString() {

            var connection = _config["ConnectionStrings: Default"];

            return connection;
        }

        


    }
}
