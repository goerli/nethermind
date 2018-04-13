﻿/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Threading;
using Nethermind.Blockchain.Difficulty;
using Nethermind.Blockchain.Validators;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test
{
    [TestFixture]
    public class SynchronizationManagerTests
    {
        [SetUp]
        public void Setup()
        {
            ISpecProvider specProvider = RopstenSpecProvider.Instance;

            _blockTree = Substitute.For<IBlockTree>();
            DifficultyCalculator difficultyCalculator = new DifficultyCalculator(specProvider);
            HeaderValidator headerValidator = new HeaderValidator(difficultyCalculator, _blockTree, new FakeSealEngine(TimeSpan.Zero), specProvider, NullLogger.Instance);
            SignatureValidator signatureValidator = new SignatureValidator(specProvider.ChainId);
            TransactionValidator transactionValidator = new TransactionValidator(signatureValidator);
            OmmersValidator ommersValidator = new OmmersValidator(_blockTree, headerValidator, NullLogger.Instance);
            BlockValidator blockValidator = new BlockValidator(transactionValidator, headerValidator, ommersValidator, specProvider, NullLogger.Instance);

            _genesisBlock = Build.A.Block.WithNumber(0).TestObject;
            _manager = new SynchronizationManager(_delay, _blockTree, headerValidator, blockValidator, transactionValidator, specProvider, _genesisBlock, _genesisBlock, _genesisBlock.Difficulty, new ConsoleLogger());
        }

        private IBlockTree _blockTree;
        private Block _genesisBlock;
        private SynchronizationManager _manager;

        private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(50);

        [Test]
        public void On_new_peer_asks_about_the_best_block()
        {
            ISynchronizationPeer peer = Substitute.For<ISynchronizationPeer>();
            peer.GetHeadBlockHash().Returns(TestObject.KeccakA);
            peer.GetHeadBlockNumber().Returns(3);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _manager.RoundFinished += (sender, args) => { resetEvent.Set(); };
            _manager.AddPeer(peer);
            _manager.Start();
            resetEvent.WaitOne(_delay * 10);
            peer.Received().GetHeadBlockHash();
            peer.Received().GetHeadBlockNumber();
        }

        [Test]
        public void On_new_peer_retrieves_missing_blocks()
        {
            ISynchronizationPeer peer = Substitute.For<ISynchronizationPeer>();
            peer.GetHeadBlockHash().Returns(TestObject.KeccakA);
            peer.GetHeadBlockNumber().Returns(3);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _manager.RoundFinished += (sender, args) => { resetEvent.Set(); };
            _manager.AddPeer(peer);
            _manager.Start();
            resetEvent.WaitOne(_delay * 10);
            peer.Received().GetBlocks(_genesisBlock.Hash, 4);
        }

        [Test]
        public void On_new_peer_adds_new_blocks_to_block_tree()
        {
            Block block1 = Build.A.Block.WithNumber(1).WithParent(_genesisBlock).TestObject;
            Block block2 = Build.A.Block.WithNumber(2).WithParent(block1).TestObject;
            Block block3 = Build.A.Block.WithNumber(3).WithParent(block2).TestObject;

            _blockTree.AddBlock(_genesisBlock).Returns(AddBlockResult.Ignored);
            _blockTree.AddBlock(block1).Returns(AddBlockResult.Added);
            _blockTree.AddBlock(block2).Returns(AddBlockResult.Added);
            _blockTree.AddBlock(block3).Returns(AddBlockResult.Added);
            
            ISynchronizationPeer peer = Substitute.For<ISynchronizationPeer>();
            peer.GetHeadBlockHash().Returns(TestObject.KeccakA);
            peer.GetHeadBlockNumber().Returns(3);
            peer.GetBlocks(_genesisBlock.Hash, 4).Returns(new [] {_genesisBlock, block1, block2, block3});

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _manager.RoundFinished += (sender, args) => { resetEvent.Set(); };
            _manager.AddPeer(peer);
            _manager.Start();
            resetEvent.WaitOne(_delay * 10);
            _blockTree.Received().AddBlock(block1);
            _blockTree.Received().AddBlock(block2);
            _blockTree.Received().AddBlock(block3);
        }
    }
}