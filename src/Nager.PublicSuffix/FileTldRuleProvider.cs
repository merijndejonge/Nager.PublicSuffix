﻿using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Nager.PublicSuffix
{
    /// <summary>
    /// FileTldRuleProvider
    /// </summary>
    public class FileTldRuleProvider : ITldRuleProvider
    {
        private readonly string _fileName;

        /// <summary>
        /// FileTldRuleProvider
        /// </summary>
        /// <param name="fileName"></param>
        public FileTldRuleProvider(string fileName)
        {
            this._fileName = fileName;
        }

        ///<inheritdoc/>
        public async Task<IEnumerable<TldRule>> BuildAsync()
        {
            var ruleData = await this.LoadFromFile().ConfigureAwait(false);

            var ruleParser = new TldRuleParser();
            var rules = ruleParser.ParseRules(ruleData);
            return rules;
        }

        private async Task<string> LoadFromFile()
        {
            if (!File.Exists(this._fileName))
            {
                throw new FileNotFoundException("Rule file does not exist");
            }

            using var reader = File.OpenText(this._fileName);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }
}
