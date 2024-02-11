﻿using Nager.PublicSuffix.DomainNormalizers;
using Nager.PublicSuffix.Exceptions;
using Nager.PublicSuffix.Extensions;
using Nager.PublicSuffix.Models;
using Nager.PublicSuffix.RuleProviders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nager.PublicSuffix
{
    /// <summary>
    /// Domain Parser
    /// </summary>
    public class DomainParser : IDomainParser
    {
        private DomainDataStructure _domainDataStructure;
        private readonly IDomainNormalizer _domainNormalizer;
        private readonly TldRule _rootTldRule = new TldRule("*");

        /// <summary>
        /// Creates and initializes a DomainParser
        /// </summary>
        /// <param name="rules">The list of rules.</param>
        /// <param name="domainNormalizer">An <see cref="IDomainNormalizer"/>.</param>
        public DomainParser(IEnumerable<TldRule> rules, IDomainNormalizer domainNormalizer = null)
            : this(domainNormalizer)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            this.AddRules(rules);
        }

        /// <summary>
        /// Creates and initializes a DomainParser
        /// </summary>
        /// <param name="ruleProvider">A rule provider from interface <see cref="ITopLevelDomainRuleProvider"/>.</param>
        /// <param name="domainNormalizer">An <see cref="IDomainNormalizer"/>.</param>
        public DomainParser(ITopLevelDomainRuleProvider ruleProvider, IDomainNormalizer domainNormalizer = null)
            : this(domainNormalizer)
        {
            var rules = ruleProvider.BuildAsync().GetAwaiter().GetResult();
            this.AddRules(rules);
        }

        /// <summary>
        /// Creates a DomainParser based on an already initialzed tree.
        /// </summary>
        /// <param name="initializedDataStructure">An already initialized tree.</param>
        /// <param name="domainNormalizer">An <see cref="IDomainNormalizer"/>.</param>
        public DomainParser(DomainDataStructure initializedDataStructure, IDomainNormalizer domainNormalizer = null)
            : this(domainNormalizer)
        {
            this._domainDataStructure = initializedDataStructure;
        }

        private DomainParser(IDomainNormalizer domainNormalizer)
        {
            this._domainNormalizer = domainNormalizer ?? new UriDomainNormalizer();
        }

        ///<inheritdoc/>
        public DomainInfo Parse(Uri domain)
        {
            var partlyNormalizedDomain = domain.Host;
            var normalizedHost = domain.GetComponents(UriComponents.NormalizedHost, UriFormat.UriEscaped); //Normalize punycode

            var parts = normalizedHost
                .Split('.')
                .Reverse()
                .ToList();

            return this.GetDomainFromParts(partlyNormalizedDomain, parts);
        }

        ///<inheritdoc/>
        public DomainInfo Parse(string domain)
        {
            var parts = this._domainNormalizer.PartlyNormalizeDomainAndExtractFullyNormalizedParts(domain, out string partlyNormalizedDomain);
            return this.GetDomainFromParts(partlyNormalizedDomain, parts);
        }

        ///<inheritdoc/>
        public bool IsValidDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return false;
            }

            if (Uri.TryCreate(domain, UriKind.Absolute, out _))
            {
                return false;
            }

            if (!Uri.TryCreate($"http://{domain}", UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.DnsSafeHost.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (domain[0] == '*')
            {
                return false;
            }

            try
            {
                var parts = this._domainNormalizer.PartlyNormalizeDomainAndExtractFullyNormalizedParts(domain, out string partlyNormalizedDomain);

                var domainName = this.GetDomainFromParts(partlyNormalizedDomain, parts);
                if (domainName == null)
                {
                    return false;
                }

                return !domainName.TopLevelDomainRule.Equals(this._rootTldRule);
            }
            catch (ParseException)
            {
                return false;
            }
        }

        private void AddRules(IEnumerable<TldRule> tldRules)
        {
            this._domainDataStructure = this._domainDataStructure ?? new DomainDataStructure("*", this._rootTldRule);

            this._domainDataStructure.AddRules(tldRules);
        }

        private DomainInfo GetDomainFromParts(string domain, List<string> parts)
        {
            if (parts == null || parts.Count == 0 || parts.Any(x => x.Equals(string.Empty)))
            {
                throw new ParseException("Invalid domain part detected");
            }

            var structure = this._domainDataStructure;
            var matches = new List<TldRule>();
            this.FindMatches(parts, structure, matches);

            //Sort so exceptions are first, then by biggest label count (with wildcards at bottom) 
            var sortedMatches = matches.OrderByDescending(x => x.Type == TldRuleType.WildcardException ? 1 : 0)
                .ThenByDescending(x => x.LabelCount)
                .ThenByDescending(x => x.Name);

            var winningRule = sortedMatches.FirstOrDefault();

            //Domain is TLD
            if (parts.Count == winningRule.LabelCount)
            {
                parts.Reverse();
                var tld = string.Join(".", parts);

                if (winningRule.Type == TldRuleType.Wildcard)
                {
                    if (tld.EndsWith(winningRule.Name.Substring(1)))
                    {
                        throw new ParseException("Domain is a TLD according publicsuffix", winningRule);
                    }
                }
                else
                {
                    if (tld.Equals(winningRule.Name))
                    {
                        throw new ParseException("Domain is a TLD according publicsuffix", winningRule);
                    }
                }

                throw new ParseException($"Unknown domain {domain}");
            }

            return new DomainInfo(domain, winningRule);
        }

        private void FindMatches(IEnumerable<string> parts, DomainDataStructure structure, List<TldRule> matches)
        {
            if (structure.TldRule != null)
            {
                matches.Add(structure.TldRule);
            }

            var part = parts.FirstOrDefault();
            if (string.IsNullOrEmpty(part))
            {
                return;
            }

            if (structure.Nested.TryGetValue(part, out DomainDataStructure foundStructure))
            {
                this.FindMatches(parts.Skip(1), foundStructure, matches);
            }

            if (structure.Nested.TryGetValue("*", out foundStructure))
            {
                this.FindMatches(parts.Skip(1), foundStructure, matches);
            }
        }
    }
}
